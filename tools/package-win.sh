#!/bin/sh
# Passenger's Windows installer: `just package-win [win-x64|win-arm64]`, on Windows (through Git's sh) or macOS, makes
# artifacts/package/Passenger-windows-x64.exe (or -arm64), the names a release keeps from one to the next (#56). The
# Release publish (self-contained, ReadyToRun, the debugger and Roslyn's server beside it) is packed by Velopack's vpk,
# at the version dotnet-tools.json pins, into a Setup.exe that installs for the person alone, with no administrator,
# into %LocalAppData%\PassengerIDE, adds a Start menu entry and an entry in Settings > Apps that uninstalls it, and runs
# Passenger.exe's install hook, which registers .sln, .slnx and .csproj (Installation.cs). Velopack's release (the full
# package and the feed its updates read, #35) stays in artifacts/package/<rid>/velopack.
#
# Signed only on Windows, with signtool, when one of these is set, and otherwise unsigned (SmartScreen then asks once,
# More info > Run anyway): PASSENGER_SIGNTOOL_PARAMS, signtool sign's parameters for a certificate (/f or /sha1, /fd
# sha256, /tr and /td sha256 for its timestamp); or PASSENGER_ARTIFACT_SIGNING_METADATA, the metadata.json of an Azure
# Artifact Signing account (Endpoint, CodeSigningAccountName, CertificateProfileName), whose identity Azure's own
# credential reads from the environment (AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET) or an `az login`.
set -eu
fail() {
    echo "package-win: $*" >&2
    exit 1
}
rid=${1:-win-x64}
case $rid in
    win-x64 | win-arm64) ;;
    *) fail "'$rid' is not a Windows system Passenger ships for: win-x64 or win-arm64" ;;
esac
# Git for Windows' sh rewrites an argument it takes for one of its own paths (signtool's /fd) on the way to a Windows
# program; every path here is made Windows' own (C:/...) instead, by its cygpath, so nothing is rewritten.
export MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*'
root=$(cd "$(dirname "$0")/.." && pwd)
if command -v cygpath >/dev/null 2>&1; then
    root=$(cygpath -m "$root")
fi
out="$root/artifacts/package"
work="$out/$rid"
publish="$work/publish"
release="$work/velopack"
installer="$out/Passenger-windows-${rid#win-}.exe"

signtool_params=${PASSENGER_SIGNTOOL_PARAMS:-}
signing_metadata=${PASSENGER_ARTIFACT_SIGNING_METADATA:-}
if [ -n "$signtool_params" ] && [ -n "$signing_metadata" ]; then
    fail "PASSENGER_SIGNTOOL_PARAMS and PASSENGER_ARTIFACT_SIGNING_METADATA are both set; sign with one"
fi
if [ -n "$signtool_params$signing_metadata" ]; then
    case $(uname -s) in
        MINGW* | MSYS* | CYGWIN*) ;;
        *) fail "signing runs Windows' signtool, so a signed installer is built on Windows; unset the signing variables for an unsigned one here" ;;
    esac
fi
if [ -n "$signing_metadata" ]; then
    [ -f "$signing_metadata" ] || fail "PASSENGER_ARTIFACT_SIGNING_METADATA names '$signing_metadata', which is not a file"
    signing_metadata=$(cygpath -m "$signing_metadata")
fi

# The version as tools/package-mac.sh takes it: a vX.Y.Z tag on this commit, else Directory.Build.props' Version.
version=$(git -C "$root" describe --tags --exact-match --match 'v[0-9]*' 2>/dev/null | sed 's/^v//' || true)
[ -n "$version" ] || version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
if ! echo "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    fail "the version '$version' is not X.Y.Z in numbers (a vX.Y.Z tag on the commit, or Version in Directory.Build.props)"
fi

rm -rf "$work" "$installer"
dotnet publish "$root/src/Passenger.App" -c Release -r "$rid" --self-contained -p:PublishReadyToRun=true -p:Version="$version" \
    -o "$publish" -nologo -v quiet

# The pack id names the install's folder, which Velopack's uninstaller empties: not Passenger, whose folder in
# %LocalAppData% is the person's settings and state, which outlive an uninstall as Rider's do. Each system's channel is
# its own, so one release can carry both feeds. SkiaSharp's and HarfBuzzSharp's native symbols (a hundred megabytes) stay
# out; Passenger's own symbols stay in, for the line numbers in its log's stack traces.
set -- --packId PassengerIDE --packVersion "$version" --packTitle Passenger --packAuthors Passenger \
    --packDir "$publish" --mainExe Passenger.exe --icon "$root/src/Passenger.App/Assets/Passenger.ico" \
    --runtime "$rid" --channel "$rid" --shortcuts StartMenuRoot --noPortable \
    --exclude 'lib(SkiaSharp|HarfBuzzSharp)\.pdb$' --outputDir "$release"
if [ -n "$signtool_params" ]; then
    set -- "$@" --signParams "$signtool_params"
elif [ -n "$signing_metadata" ]; then
    set -- "$@" --azureTrustedSignFile "$signing_metadata"
fi
cd "$root"
dotnet tool restore
dotnet vpk '[win]' pack --skip-updates "$@"

setup=$(find "$release" -maxdepth 1 -name '*-Setup.exe')
[ "$(echo "$setup" | grep -c .)" = 1 ] || fail "vpk was to write one Setup.exe in $release, and wrote: ${setup:-none}"
cp "$setup" "$installer"
echo "package: $installer ($( [ -n "$signtool_params$signing_metadata" ] && echo "signed" || echo "unsigned: SmartScreen asks once, More info > Run anyway"))"

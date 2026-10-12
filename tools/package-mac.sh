#!/bin/sh
# A macOS app bundle of Passenger: `just package-mac [osx-arm64|osx-x64]`. The Release publish (self-contained,
# ReadyToRun, the debugger and Roslyn's server beside it) goes into Passenger.app/Contents/MacOS with an Info.plist,
# then the bundle is signed under the hardened runtime with the entitlements a debugger needs: by
# PASSENGER_SIGN_IDENTITY (a Developer ID Application identity), else ad hoc, which runs on this Mac only. With
# PASSENGER_NOTARY_PROFILE (a notarytool keychain profile) it is notarized and stapled. The zip lands in
# artifacts/package.
set -eu
rid=${1:-osx-arm64}
root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/artifacts/package"
publish="$out/publish-$rid"
app="$out/Passenger.app"
# The version is a vX.Y.Z tag on this commit, else the Version every project carries (Directory.Build.props), and
# the assemblies are built with it too, so the app says the version its bundle does. The build number counts the
# commits, so each package's is higher than the last; a shallow clone would count only the ones it has.
version=$(git -C "$root" describe --tags --exact-match --match 'v[0-9]*' 2>/dev/null | sed 's/^v//' || true)
[ -n "$version" ] || version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
if ! echo "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "package-mac: the version '$version' is not X.Y.Z in numbers (a vX.Y.Z tag on the commit, or Version in Directory.Build.props)" >&2
    exit 1
fi
if [ "$(git -C "$root" rev-parse --is-shallow-repository)" = true ]; then
    echo "package-mac: a shallow clone cannot count its commits for the build number; fetch the whole history" >&2
    exit 1
fi
build=$(git -C "$root" rev-list --count HEAD)

rm -rf "$publish" "$app"
dotnet publish "$root/src/Passenger.App" -c Release -r "$rid" --self-contained -p:PublishReadyToRun=true -p:Version="$version" \
    -o "$publish" -nologo -v quiet
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
cp "$root/src/Passenger.App/Assets/Passenger.icns" "$app/Contents/Resources/Passenger.icns"
cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Passenger</string>
    <key>CFBundleDisplayName</key><string>Passenger</string>
    <key>CFBundleIdentifier</key><string>io.github.zaneris.passenger</string>
    <key>CFBundleExecutable</key><string>Passenger</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleIconFile</key><string>Passenger</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$build</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
</dict>
</plist>
EOF

# A debugger attaches to and launches other processes, the JIT writes code, and the native libraries (Skia,
# dbgshim, the pseudo-terminal's shim) are not signed by Passenger's team. Never get-task-allow, which lets anything
# attach to Passenger itself and which Apple's notary refuses.
entitlements="$out/entitlements.plist"
cat > "$entitlements" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key><true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
    <key>com.apple.security.cs.disable-library-validation</key><true/>
    <key>com.apple.security.cs.allow-dyld-environment-variables</key><true/>
    <key>com.apple.security.cs.debugger</key><true/>
</dict>
</plist>
EOF
# Ad hoc signing takes the same hardened runtime and entitlements, so a package made here runs as the shipped one
# does; only a Developer ID signature is timestamped.
identity=${PASSENGER_SIGN_IDENTITY:--}
sign() {
    if [ "$identity" = "-" ]; then
        codesign --force --options runtime --sign - "$@"
    else
        codesign --force --options runtime --timestamp --sign "$identity" "$@"
    fi
}
# Inside out, then the bundle, which signs its main executable. The bundle's seal takes everything in Contents/MacOS
# for code, so .NET's DLLs and PDBs are signed too (their signatures kept in extended attributes); the entitlements are
# only the executables'.
find "$app/Contents/MacOS" -type f ! -path "$app/Contents/MacOS/Passenger" | while IFS= read -r file; do
    case $(file -b "$file") in
        Mach-O*executable*) sign --entitlements "$entitlements" "$file" ;;
        *) sign "$file" ;;
    esac
done
sign --entitlements "$entitlements" "$app"
codesign --verify --deep --strict "$app"

zip="$out/Passenger-$version-$rid.zip"
rm -f "$zip"
ditto -c -k --keepParent "$app" "$zip"
if [ -n "${PASSENGER_NOTARY_PROFILE:-}" ]; then
    # The submission's status, not notarytool's exit, says whether the notary accepted the app; its log says why not.
    result=$(xcrun notarytool submit "$zip" --keychain-profile "$PASSENGER_NOTARY_PROFILE" --wait --output-format json)
    status=$(echo "$result" | plutil -extract status raw -o - -)
    if [ "$status" != Accepted ]; then
        echo "package-mac: notarization came back $status" >&2
        xcrun notarytool log "$(echo "$result" | plutil -extract id raw -o - -)" --keychain-profile "$PASSENGER_NOTARY_PROFILE" >&2
        exit 1
    fi
    xcrun stapler staple "$app"
    rm -f "$zip"
    ditto -c -k --keepParent "$app" "$zip"
fi
echo "package: $zip ($( [ "$identity" = "-" ] && echo "signed ad hoc, for this Mac" || echo "signed by $identity" ))"

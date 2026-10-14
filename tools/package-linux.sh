#!/bin/sh
# Passenger for Linux: `just package-linux [linux-x64|linux-arm64]` makes artifacts/package/Passenger-linux-x64.tar.gz (or
# -arm64), the names a release keeps from one to the next (#56, #57), as site/linux.html tells people to use it. It holds
# one passenger/ folder: the Release publish (self-contained, ReadyToRun, the debugger and Roslyn's server beside it) in
# app/, the `passenger` launcher that runs it from wherever the folder is, install.sh (for one person, or --system for
# everyone, or --uninstall), and the desktop entry's template, its icon and the file types it opens, which install.sh
# puts in place (tools/linux). The archive is then unpacked again and checked: its layout, every ELF file built for its
# processor, and the launcher running the app. Made on Linux (CI's) or macOS.
set -eu
fail() {
    echo "package-linux: $*" >&2
    exit 1
}
rid=${1:-linux-x64}
case $rid in
    linux-x64) machine=x86-64 ;;
    linux-arm64) machine=aarch64 ;;
    *) fail "'$rid' is not a Linux system Passenger ships for: linux-x64 or linux-arm64" ;;
esac
command -v file > /dev/null 2>&1 || fail "the check needs file(1), to read each ELF file's processor"
app=io.github.zaneris.passenger

root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/artifacts/package"
work="$out/$rid"
publish="$work/publish"
stage="$work/stage"
folder="$stage/passenger"
check="$work/check"
archive="$out/Passenger-linux-${rid#linux-}.tar.gz"

# The version as tools/package-mac.sh takes it: a vX.Y.Z tag on this commit, else Directory.Build.props' Version.
version=$(git -C "$root" describe --tags --exact-match --match 'v[0-9]*' 2>/dev/null | sed 's/^v//' || true)
[ -n "$version" ] || version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props" | head -n 1)
if ! echo "$version" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    fail "the version '$version' is not X.Y.Z in numbers (a vX.Y.Z tag on the commit, or Version in Directory.Build.props)"
fi

rm -rf "$work" "$archive"
mkdir -p "$work"
# Only the packages the lock files pin, as CI restores; and no MSBuild node is left running once it is done.
dotnet publish "$root/src/Passenger.App" -c Release -r "$rid" --self-contained -p:PublishReadyToRun=true -p:Version="$version" \
    -p:RestoreLockedMode=true -nodeReuse:false -o "$publish" -nologo -v quiet

linux="$root/tools/linux"
mkdir -p "$folder"
cp -R "$publish" "$folder/app"
cp "$linux/passenger" "$linux/install.sh" "$linux/$app.desktop" "$linux/$app.xml" "$folder/"
cp "$root/src/Passenger.App/Assets/passenger.png" "$folder/$app.png"
cp "$root/src/Passenger.App/Assets/passenger.svg" "$folder/$app.svg"
chmod 755 "$folder/passenger" "$folder/install.sh"
chmod 644 "$folder/$app.desktop" "$folder/$app.xml" "$folder/$app.png" "$folder/$app.svg"

# Owned by root in the archive, so an install for everyone from it never keeps the builder's user and group; files
# readable by everyone, as an install for everyone needs. GNU tar's flags, or bsdtar's (macOS), which must leave out
# extended attributes and the AppleDouble files that would carry them.
chmod -R u+rwX,go+rX,go-w "$folder"
if tar --version 2>/dev/null | grep -q 'GNU tar'; then
    tar -C "$stage" --owner=0 --group=0 --numeric-owner --sort=name -czf "$archive" passenger
else
    COPYFILE_DISABLE=1 tar -C "$stage" --uid 0 --gid 0 --no-xattrs --no-mac-metadata -czf "$archive" passenger
fi

# The archive as a person gets it: one passenger/ folder, unpacked; the launcher, install.sh, the desktop entry's
# pieces, the app, Roslyn's server, the debugger's adapter and dbgshim, the pseudo-terminal's shim, the licence and the
# notices in it; every ELF file built for its processor (an x64 file in an Arm64 build would fail only when loaded).
others=$(tar -tzf "$archive" | grep -v '^passenger/' || true)
[ -z "$others" ] || fail "the archive holds more than the passenger/ folder: $(echo "$others" | head -n 3)"
mkdir -p "$check"
tar -C "$check" -xzf "$archive"
shipped="$check/passenger"
for part in passenger install.sh app/Passenger app/createdump; do
    [ -x "$shipped/$part" ] || fail "the archive has no executable $part"
done
for part in "$app.desktop" "$app.xml" "$app.png" "$app.svg" app/Passenger.dll app/libSkiaSharp.so app/libHarfBuzzSharp.so \
    app/libporta_pty.so app/LICENSE.txt app/THIRD-PARTY-NOTICES.txt app/roslyn/Microsoft.CodeAnalysis.LanguageServer.dll \
    app/roslyn-extension/Passenger.Roslyn.Extension.dll app/debugger/Passenger.DebugAdapter.dll app/debugger/libdbgshim.so; do
    [ -f "$shipped/$part" ] || fail "the archive has no $part"
done
grep -q '^StartupWMClass=Passenger$' "$shipped/$app.desktop" || fail "the desktop entry does not match the window's class, Passenger"
find "$shipped" -type f -exec file -F '|' {} + | grep '| *ELF' > "$work/elf" || true
[ -s "$work/elf" ] || fail "the archive has no ELF file"
wrong=$(grep -v "$machine" "$work/elf" | cut -d'|' -f1 | sed "s:^$shipped/::" || true)
[ -z "$wrong" ] || fail "built for another processor than $rid's: $(echo "$wrong" | head -n 5 | tr '\n' ' ')"
# On a Linux machine of the archive's processor, the launcher runs the app, which draws its window off-screen (Skia,
# HarfBuzz and the fonts loaded) with its state in the work folder.
if [ "$(uname -s)" = Linux ] && [ "$(uname -m)" = "$(echo "$machine" | tr - _)" ]; then
    PASSENGER_SETTINGS="$work/state" "$shipped/passenger" --screenshot "$work/welcome.png" --size 1000x700 > /dev/null \
        || fail "the launcher could not run the app"
    [ -s "$work/welcome.png" ] || fail "the app ran but drew no window"
fi

size=$(du -h "$archive" | cut -f1 | tr -d ' ')
echo "package: $archive ($size, Passenger $version for $rid)"

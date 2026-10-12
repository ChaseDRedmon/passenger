#!/bin/sh
# A macOS app bundle of Passenger: `just package-mac [osx-arm64|osx-x64]`. The Release publish (self-contained,
# ReadyToRun, the debugger and Roslyn's server beside it) goes into Passenger.app/Contents/MacOS with an Info.plist,
# then the bundle is signed: with PASSENGER_SIGN_IDENTITY (a Developer ID Application identity) under the hardened
# runtime and the entitlements a debugger needs, else ad hoc, which runs on this Mac only. With
# PASSENGER_NOTARY_PROFILE (a notarytool keychain profile) it is notarized and stapled. The zip lands in
# artifacts/package.
set -eu
rid=${1:-osx-arm64}
root=$(cd "$(dirname "$0")/.." && pwd)
out="$root/artifacts/package"
publish="$out/publish-$rid"
app="$out/Passenger.app"
version=$(git -C "$root" describe --tags --always --dirty 2>/dev/null || echo 0.0.0)
short=$(echo "$version" | sed -E 's/^v//; s/[^0-9.].*$//')
[ -n "$short" ] || short=0.0.0

rm -rf "$publish" "$app"
dotnet publish "$root/src/Passenger.App" -c Release -r "$rid" --self-contained -p:PublishReadyToRun=true -o "$publish" -nologo -v quiet
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
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
    <key>CFBundleShortVersionString</key><string>$short</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
</dict>
</plist>
EOF

# A debugger attaches to and launches other processes, the JIT writes code, and the native libraries (Skia,
# dbgshim, the pseudo-terminal's shim) are not signed by Passenger's team.
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
    <key>com.apple.security.get-task-allow</key><true/>
</dict>
</plist>
EOF
identity=${PASSENGER_SIGN_IDENTITY:--}
if [ "$identity" = "-" ]; then
    codesign --force --deep --sign - "$app"
else
    # Inside out: every native library and executable, then the bundle.
    find "$app/Contents/MacOS" -type f \( -name "*.dylib" -o -perm -u+x \) -print0 |
        xargs -0 codesign --force --timestamp --options runtime --entitlements "$entitlements" --sign "$identity"
    codesign --force --timestamp --options runtime --entitlements "$entitlements" --sign "$identity" "$app"
fi
codesign --verify --deep --strict "$app"

zip="$out/Passenger-$version-$rid.zip"
rm -f "$zip"
ditto -c -k --keepParent "$app" "$zip"
if [ -n "${PASSENGER_NOTARY_PROFILE:-}" ]; then
    xcrun notarytool submit "$zip" --keychain-profile "$PASSENGER_NOTARY_PROFILE" --wait
    xcrun stapler staple "$app"
    rm -f "$zip"
    ditto -c -k --keepParent "$app" "$zip"
fi
echo "package: $zip ($( [ "$identity" = "-" ] && echo "signed ad hoc, for this Mac" || echo "signed by $identity" ))"

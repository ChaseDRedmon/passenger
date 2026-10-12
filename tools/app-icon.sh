#!/bin/sh
# Passenger's own icon from its source, src/Passenger.App/Assets/passenger.svg: `just app-icon`. QuickLook draws the SVG
# at each size macOS asks of an app icon, iconutil packs them into Passenger.icns (the bundle's, which
# tools/package-mac.sh copies in), and the 256-pixel one is the window's icon. macOS only, since QuickLook draws it.
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
assets="$root/src/Passenger.App/Assets"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
set_dir="$work/Passenger.iconset"
mkdir -p "$set_dir"
qlmanage -t -s 1024 -o "$work" "$assets/passenger.svg" >/dev/null 2>&1
master="$work/passenger.svg.png"
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$master" --out "$set_dir/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$master" --out "$set_dir/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$set_dir" -o "$assets/Passenger.icns"
sips -z 256 256 "$master" --out "$assets/passenger.png" >/dev/null
echo "app-icon: $assets/Passenger.icns and passenger.png"

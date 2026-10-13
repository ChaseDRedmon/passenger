#!/bin/sh
# Passenger's own icon from its source, src/Passenger.App/Assets/passenger.svg: `just app-icon`. QuickLook draws the SVG
# at each size macOS asks of an app icon, iconutil packs them into Passenger.icns (the bundle's, which
# tools/package-mac.sh copies in), and the 256-pixel one is the window's icon. Passenger.ico is Windows' (Passenger.exe's,
# so its Start menu entry's and the file types', and the installer's): the sizes the shell draws, each a PNG inside, as
# Windows reads them since Vista. macOS only, since QuickLook draws it.
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
ico_sizes="16 20 24 32 40 48 64 256"
for size in $ico_sizes; do
    sips -z "$size" "$size" "$master" --out "$work/ico-$size.png" >/dev/null
done
# An ICONDIR, an entry of 16 bytes per image (a width or height of 256 is written 0), then the PNGs themselves.
python3 -I - "$assets/Passenger.ico" $(for size in $ico_sizes; do echo "$work/ico-$size.png"; done) <<'PY'
import struct, sys
images = [open(path, "rb").read() for path in sys.argv[2:]]
offset = 6 + 16 * len(images)
head = struct.pack("<HHH", 0, 1, len(images))
for png in images:
    width, height = struct.unpack(">II", png[16:24])
    head += struct.pack("<BBBBHHII", width % 256, height % 256, 0, 0, 1, 32, len(png), offset)
    offset += len(png)
with open(sys.argv[1], "wb") as ico:
    ico.write(head + b"".join(images))
PY
echo "app-icon: $assets/Passenger.icns, passenger.png and Passenger.ico"

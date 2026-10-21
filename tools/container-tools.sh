#!/bin/sh
# Passenger's container tools for one Linux system (src/Passenger.ContainerTools), self-contained, into
# artifacts/container-tools/<rid>/, where a dev build of the IDE finds them; --archive also packs them as the release
# carries them, artifacts/container-tools/Passenger-container-tools-<rid>.tar.gz (the folder's files at its root).
# With no RID, the glibc one for the container engine's processor (docker version), else this machine's.
#   tools/container-tools.sh [linux-x64|linux-arm64|linux-musl-x64|linux-musl-arm64] [--archive]
set -eu
cd "$(dirname "$0")/.."

rid=
archive=no
for arg in "$@"; do
    case $arg in
        --archive) archive=yes ;;
        linux-x64 | linux-arm64 | linux-musl-x64 | linux-musl-arm64)
            if [ -n "$rid" ]; then
                echo "One system at a time, not $rid and $arg" >&2
                exit 2
            fi
            rid=$arg
            ;;
        *)
            echo "Unknown argument '$arg': a RID (linux-x64, linux-arm64, linux-musl-x64, linux-musl-arm64) or --archive" >&2
            exit 2
            ;;
    esac
done

if [ -z "$rid" ]; then
    processor=$(docker version --format '{{.Server.Arch}}' 2>/dev/null || uname -m)
    case $processor in
        amd64 | x86_64) rid=linux-x64 ;;
        arm64 | aarch64) rid=linux-arm64 ;;
        *)
            echo "No container tools are made for a $processor engine" >&2
            exit 1
            ;;
    esac
fi

out=artifacts/container-tools/$rid
rm -rf "$out"
AVALONIA_TELEMETRY_OPTOUT=1 dotnet publish src/Passenger.ContainerTools -c Release -r "$rid" --self-contained -o "$out" -nologo -v:q
test -x "$out/passenger-tools"
echo "$out: $(du -sh "$out" | cut -f1)"

if [ "$archive" = yes ]; then
    tarball=artifacts/container-tools/Passenger-container-tools-$rid.tar.gz
    # Owned by root, as what docker cp puts in a container is (GNU tar names it --owner, bsdtar --uid).
    tar -czf "$tarball" -C "$out" --owner=0 --group=0 . 2>/dev/null || tar -czf "$tarball" -C "$out" --uid 0 --gid 0 .
    echo "$tarball: $(du -h "$tarball" | cut -f1)"
fi

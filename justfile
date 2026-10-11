# Passenger's recipes. `just --list` shows them all.

set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

app := "src/Passenger.App"

# Build everything (Debug). Warnings are errors, and the AOT analyzers run on every build.
build:
    dotnet build

# Run the IDE (Debug, JIT): just run [--theme dark|light|system]
run *args:
    dotnet run --project {{app}} -- {{args}}

# Draw the main window off-screen to a PNG: just screenshot out.png [--size 1600x1000] [--scale 2] [--theme light]
screenshot path *args:
    dotnet run --project {{app}} -- --screenshot {{path}} {{args}}

# The AOT toolchain runs xcodebuild, which needs full Xcode; with only the Command Line Tools
# selected, the first Xcode in /Applications is used for this build alone.
[doc("Native AOT build for macOS")]
[macos]
publish-mac rid="osx-arm64":
    #!/usr/bin/env bash
    set -euo pipefail
    if ! xcodebuild -version >/dev/null 2>&1; then
        xcode=$(ls -d /Applications/Xcode*.app 2>/dev/null | head -1)
        if [ -z "$xcode" ]; then
            echo "publish-mac: Native AOT on macOS needs Xcode installed" >&2
            exit 1
        fi
        export DEVELOPER_DIR="$xcode/Contents/Developer"
    fi
    dotnet publish {{app}} -r {{rid}} -c Release

# AOT cannot cross operating systems, so this runs on Windows.
[doc("Native AOT build for Windows")]
[windows]
publish-win rid="win-x64":
    dotnet publish {{app}} -r {{rid}} -c Release

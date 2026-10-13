# Passenger's recipes. `just --list` shows them all.

set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

app := "src/Passenger.App"

# Avalonia's build task (Avalonia.BuildServices) sends usage data from every build of an Avalonia project unless this
# is in the environment, the only place it looks; every recipe here builds with it set.
export AVALONIA_TELEMETRY_OPTOUT := "1"

# Build everything (Debug). Warnings are errors.
build:
    dotnet build

# Every test: the IDE driven whole, headless. A filter narrows them: just test Editing
test filter="":
    dotnet test tests/Passenger.Tests {{ if filter == "" { "" } else { "--filter " + filter } }}

# Run the IDE (Debug, JIT): just run [--theme dark|light|system]
run *args:
    dotnet run --project {{app}} -- {{args}}

# Draw the main window off-screen to a PNG: just screenshot out.png [--size 1600x1000] [--scale 2] [--theme light]
screenshot path *args:
    dotnet run --project {{app}} -- --screenshot {{path}} {{args}}

# A self-contained Release build for one platform, precompiled with ReadyToRun for startup:
# just publish osx-arm64 | osx-x64 | win-x64 | win-arm64
publish rid:
    dotnet publish {{app}} -c Release -r {{rid}} --self-contained -p:PublishReadyToRun=true

# The macOS disk image, artifacts/package/Passenger-macos-arm64.dmg (or -x64): the app signed ad hoc, or with
# PASSENGER_SIGN_IDENTITY and then, with PASSENGER_NOTARY_PROFILE, the app and the image notarized and stapled; checked
# mounted. --dry-run writes what those two would run instead: just package-mac [osx-arm64|osx-x64] [--dry-run]
package-mac *args:
    tools/package-mac.sh {{args}}

# Passenger's own icon (Assets/Passenger.icns and passenger.png) from Assets/passenger.svg; macOS, through QuickLook.
app-icon:
    tools/app-icon.sh

# Rewrite Theme/Icons.axaml from the list in tools/icons.py (fetches any icon not yet cached).
icons:
    python3 -I tools/icons.py

# Fail naming any package the solution restores, or Roslyn's server carries, that THIRD_PARTY_NOTICES.md does not name.
notices:
    python3 -I tools/check-notices.py

# Rewrite the terminal's table of wide characters from the Unicode Character Database (tools/unicode-widths.py).
unicode-widths:
    python3 -I tools/unicode-widths.py

# Passenger as an MCP server over a folder, its tools on standard input and output, run from a clone of the
# last build so rebuilding Passenger never changes the files under it (tools/mcp.sh). `.mcp.json` starts it.
mcp folder="":
    @tools/mcp.sh {{folder}}

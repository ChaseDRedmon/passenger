# Passenger's recipes. `just --list` shows them all.

set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

app := "src/Passenger.App"

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

# Passenger.app for macOS, signed (PASSENGER_SIGN_IDENTITY, else ad hoc) and notarized with PASSENGER_NOTARY_PROFILE,
# zipped into artifacts/package: just package-mac [osx-arm64|osx-x64]
package-mac rid="osx-arm64":
    tools/package-mac.sh {{rid}}

# Rewrite Theme/Icons.axaml from the list in tools/icons.py (fetches any icon not yet cached).
icons:
    python3 -I tools/icons.py

# Passenger as an MCP server over a folder, its tools on standard input and output, run from a clone of the
# last build so rebuilding Passenger never changes the files under it (tools/mcp.sh). `.mcp.json` starts it.
mcp folder="":
    @tools/mcp.sh {{folder}}

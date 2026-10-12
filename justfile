# Passenger's recipes. `just --list` shows them all.

set windows-shell := ["powershell.exe", "-NoLogo", "-Command"]

app := "src/Passenger.App"

# Build everything (Debug). Warnings are errors.
build:
    dotnet build

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

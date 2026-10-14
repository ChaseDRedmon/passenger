#!/usr/bin/env python3
"""Fails, naming each one, when a package Passenger restores or carries is missing from THIRD_PARTY_NOTICES.md.

The packages are every one the solution's projects use, transitive ones included (`dotnet list package
--include-transitive`, so the solution must be restored first), every one Roslyn's language server carries under
roslyn/ (the .deps.json files in its restored package), every npm package the web language servers' lock file pins
(src/Passenger.Lsp/WebServers/package-lock.json, carried under web/) and the tests' own does
(tests/Passenger.Tests/NodePackages/package-lock.json), and every tool the repository's tool manifest pins
(dotnet-tools.json). A package is named in the notices by its id in backticks. CI runs this after its restore;
`just notices` runs it here.
"""
import glob
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NOTICES = os.path.join(ROOT, "THIRD_PARTY_NOTICES.md")

# Restored with the packages that use them, but in no app Passenger is published as (macOS and Windows): their natives
# are for Linux, the browser and 32-bit Windows. A Linux build (#57) moves its own into the notices.
NOT_SHIPPED = {
    "HarfBuzzSharp.NativeAssets.Linux",
    "HarfBuzzSharp.NativeAssets.WebAssembly",
    "SkiaSharp.NativeAssets.Linux",
    "SkiaSharp.NativeAssets.WebAssembly",
    "Microsoft.Diagnostics.DbgShim.linux-arm",
    "Microsoft.Diagnostics.DbgShim.linux-arm64",
    "Microsoft.Diagnostics.DbgShim.linux-musl-arm",
    "Microsoft.Diagnostics.DbgShim.linux-musl-arm64",
    "Microsoft.Diagnostics.DbgShim.linux-musl-x64",
    "Microsoft.Diagnostics.DbgShim.linux-x64",
    "Microsoft.Diagnostics.DbgShim.win-x86",
}


def dotnet(*args):
    result = subprocess.run(["dotnet", *args], cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
    if result.returncode != 0:
        sys.exit(f"check-notices: dotnet {' '.join(args)} failed:\n{result.stdout}{result.stderr}")
    return result.stdout


def solution_packages():
    """Each package id the solution's projects use, with the projects that use it."""
    listed = json.loads(dotnet("list", "Passenger.slnx", "package", "--include-transitive", "--format", "json", "--no-restore"))
    used = {}
    for project in listed.get("projects", []):
        name = os.path.splitext(os.path.basename(project["path"]))[0]
        for framework in project.get("frameworks", []):
            for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
                used.setdefault(package["id"], set()).add(name)
    if not used:
        sys.exit("check-notices: dotnet list package found no packages; restore the solution first")
    return used


def roslyn_packages():
    """Each package id the restored Roslyn language server's .deps.json files name."""
    properties = json.loads(dotnet("msbuild", "src/Passenger.Lsp/Passenger.Lsp.csproj", "-getProperty:RoslynServerRid",
                                   "-getProperty:RoslynServerVersion", "-getProperty:NuGetPackageRoot"))["Properties"]
    rid, version, packages = properties["RoslynServerRid"], properties["RoslynServerVersion"], properties["NuGetPackageRoot"]
    folder = os.path.join(packages, f"roslyn-language-server.{rid.lower()}", version.lower())
    deps = glob.glob(os.path.join(folder, "tools", "*", rid, "**", "*.deps.json"), recursive=True)
    if not deps:
        sys.exit(f"check-notices: no .deps.json under {folder}; restore the solution first")
    used = {}
    for path in deps:
        with open(path, encoding="utf-8-sig") as f:
            libraries = json.load(f).get("libraries", {})
        for name, library in libraries.items():
            if library.get("type") == "package":
                used.setdefault(name.split("/", 1)[0], set()).add("roslyn/")
    return used


def web_packages():
    """Each npm package the web language servers' lock file pins, carried under web/, and each the tests' own lock file
    pins (their project's own ESLint and Prettier)."""
    used = {}
    for lock, user in [(("src", "Passenger.Lsp", "WebServers"), "web/"), (("tests", "Passenger.Tests", "NodePackages"), "the tests' node_modules")]:
        with open(os.path.join(ROOT, *lock, "package-lock.json"), encoding="utf-8") as f:
            packages = json.load(f).get("packages", {})
        for place, package in packages.items():
            if place and not package.get("dev") and not package.get("link"):
                used.setdefault(place.rsplit("node_modules/", 1)[-1], set()).add(user)
    return used


def tool_packages():
    """Each package id the tool manifest pins: what the build runs (vpk packs the Windows installer)."""
    with open(os.path.join(ROOT, "dotnet-tools.json"), encoding="utf-8") as f:
        tools = json.load(f).get("tools", {})
    return {name: {"dotnet-tools.json"} for name in tools}


def named_in_notices():
    with open(NOTICES, encoding="utf-8") as f:
        text = f.read()
    return {name.strip().lower() for name in re.findall(r"`([^`\n]+)`", text)}


def main():
    used = solution_packages()
    for package, users in [*roslyn_packages().items(), *web_packages().items(), *tool_packages().items()]:
        used.setdefault(package, set()).update(users)
    named = named_in_notices()
    skipped = {package.lower() for package in NOT_SHIPPED}
    missing = sorted((package for package in used if package.lower() not in named and package.lower() not in skipped),
                     key=str.lower)
    if not missing:
        print(f"check-notices: THIRD_PARTY_NOTICES.md names all {len(used)} packages")
        return
    annotate = os.environ.get("GITHUB_ACTIONS") == "true"
    print(f"check-notices: THIRD_PARTY_NOTICES.md does not name {len(missing)} package(s) Passenger uses; add each with "
          "its licence and copyright as its package states them:")
    for package in missing:
        line = f"{package} (used by {', '.join(sorted(used[package]))})"
        print(f"  {line}")
        if annotate:
            print(f"::error file=THIRD_PARTY_NOTICES.md,title=Package missing from the notices::{line}")
    sys.exit(1)


if __name__ == "__main__":
    main()

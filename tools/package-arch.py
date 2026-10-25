#!/usr/bin/env python3
"""Render the AUR recipe from both local Linux release archives, without copying them."""
import argparse
import hashlib
from pathlib import Path
import re
import stat
import sys


def generate(version, assets, output):
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version):
        raise ValueError("version must be numeric X.Y.Z")
    replacements = {"@VERSION@": version}
    for architecture, token in (("x64", "@SHA256_X64@"), ("arm64", "@SHA256_ARM64@")):
        archive = assets / f"Passenger-linux-{architecture}.tar.gz"
        if not stat.S_ISREG(archive.lstat().st_mode) or archive.stat().st_size == 0:
            raise ValueError(f"not a nonempty regular archive: {archive}")
        digest = hashlib.sha256()
        with archive.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
        replacements[token] = digest.hexdigest()
    recipe = (Path(__file__).parent / "linux/arch/PKGBUILD.in").read_text(encoding="utf-8")
    for token, value in replacements.items():
        recipe = recipe.replace(token, value)
    output.mkdir(parents=True, exist_ok=True)
    destination = output / "PKGBUILD"
    if destination.is_symlink():
        raise ValueError(f"refusing symlink: {destination}")
    destination.write_text(recipe, encoding="utf-8", newline="\n")
    print(f"Wrote {destination}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("assets", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    try:
        generate(args.version, args.assets, args.output)
    except (OSError, ValueError) as error:
        sys.exit(f"package-arch: {error}")


if __name__ == "__main__":
    main()

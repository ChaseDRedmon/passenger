#!/usr/bin/env python3
"""Publish an already validated PKGBUILD and .SRCINFO, without executing the recipe."""
import argparse
from pathlib import Path
import re
import stat
import subprocess
import sys

REPOSITORY = "ssh://aur@aur.archlinux.org/passenger-ide-bin.git"
FILES = ("PKGBUILD", ".SRCINFO")
PACKAGE = "passenger-ide-bin"


def regular_bytes(path):
    if not stat.S_ISREG(path.lstat().st_mode):
        raise ValueError(f"not a regular file: {path}")
    return path.read_bytes()


def metadata(text, recipe=False):
    values = {}
    for line in text.splitlines():
        match = re.match(r"^\s*(pkgbase|pkgname|pkgver|pkgrel|epoch)\s*=\s*(.*?)\s*$", line)
        if not match:
            continue
        key, value = match.groups()
        if key in values or key == "epoch":
            raise ValueError(f"duplicate or unexpected metadata: {key}")
        if recipe and len(value) >= 2 and value[0] in "\"'" and value[-1] == value[0]:
            value = value[1:-1]
        values[key] = value
    required = {"pkgname", "pkgver", "pkgrel"} | (set() if recipe else {"pkgbase"})
    if not required <= values.keys():
        raise ValueError("missing package metadata")
    if values["pkgname"] != PACKAGE or values.get("pkgbase", PACKAGE) != PACKAGE:
        raise ValueError(f"pkgbase and pkgname must be {PACKAGE}")
    version = values["pkgver"]
    release = values["pkgrel"]
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) or not re.fullmatch(r"[1-9][0-9]*", release):
        raise ValueError("expected stable numeric X.Y.Z and a positive integer pkgrel")
    return tuple(map(int, version.split("."))), int(release)


def git(checkout, *arguments, check=True):
    return subprocess.run(["git", "-C", str(checkout), *arguments], check=check,
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


def publish(recipe, checkout, repository):
    if {path.name for path in recipe.iterdir()} != set(FILES):
        raise ValueError("recipe directory must contain only PKGBUILD and .SRCINFO")
    content = {name: regular_bytes(recipe / name) for name in FILES}
    incoming = metadata(content[".SRCINFO"].decode("utf-8"))
    if metadata(content["PKGBUILD"].decode("utf-8"), recipe=True) != incoming:
        raise ValueError("PKGBUILD and .SRCINFO metadata disagree")
    if checkout.is_symlink() or (checkout.exists() and (not checkout.is_dir() or any(checkout.iterdir()))):
        raise ValueError("checkout destination must be absent or an empty directory")
    checkout.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["git", "clone", "--no-checkout", "--", repository, str(checkout)], check=True)
    master = git(checkout, "show-ref", "--verify", "--quiet", "refs/remotes/origin/master", check=False)
    if master.returncode == 0:
        git(checkout, "checkout", "-B", "master", "refs/remotes/origin/master")
        previous = {name: regular_bytes(checkout / name) for name in FILES}
        current = metadata(previous[".SRCINFO"].decode("utf-8"))
        if incoming < current:
            raise ValueError("refusing a version or pkgrel downgrade")
        if content == previous:
            print("AUR recipe is already identical; nothing to publish")
            return
        if incoming == current:
            raise ValueError("refusing changed recipe bytes at the same version and pkgrel")
    elif master.returncode == 1:
        if git(checkout, "rev-list", "--all").stdout.strip():
            raise ValueError("repository has commits but no master branch")
        git(checkout, "symbolic-ref", "HEAD", "refs/heads/master")
    else:
        raise ValueError("could not inspect the remote master branch")
    for name in FILES:
        (checkout / name).write_bytes(content[name])
    git(checkout, "add", "--", *FILES)
    version, release = incoming
    message = f"Update {PACKAGE} to {'.'.join(map(str, version))}-{release}\n"
    subprocess.run(["git", "-C", str(checkout), "commit", "-F", "-", "--", *FILES],
                   input=message, text=True, check=True)
    git(checkout, "push", "origin", "HEAD:master")
    print("Published PKGBUILD and .SRCINFO to master")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe", type=Path)
    parser.add_argument("checkout", type=Path)
    parser.add_argument("--repository", default=REPOSITORY)
    args = parser.parse_args()
    try:
        publish(args.recipe.resolve(), args.checkout.absolute(), args.repository)
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        detail = error.stderr if isinstance(error, subprocess.CalledProcessError) and error.stderr else str(error)
        sys.exit(f"publish-aur: {detail}")


if __name__ == "__main__":
    main()

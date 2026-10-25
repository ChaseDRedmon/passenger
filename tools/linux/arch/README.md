# Passenger IDE on Arch Linux

`passenger-ide-bin` repackages Passenger's self-contained GitHub release archives. It supports x86_64 and aarch64.
The AUR holds the build recipe and `.SRCINFO`, not the binaries. The package installs under `/opt/passenger-ide` and
provides `passenger-ide`, avoiding the unrelated Phusion `passenger` command. It does not provide Phusion's package.

## Build locally

On Arch Linux, install `base-devel`, Python, and Git. From a Passenger checkout, download both Linux archives from the
same stable release into a directory, then generate its recipe:

```sh
python3 tools/package-arch.py 0.2.0 /path/to/release/assets artifacts/package/aur
cd artifacts/package/aur
makepkg --printsrcinfo > .SRCINFO
makepkg -si
```

The downloads must be named `Passenger-linux-x64.tar.gz` and `Passenger-linux-arm64.tar.gz`. Use trusted release
assets; the generator hashes exactly those files. `makepkg` downloads the version-specific archive for the current
machine and verifies that checksum. It never runs the archive's `install.sh`. A .NET SDK (10 or later) is needed for
project builds and C# language support; a user-installed SDK works too. Node.js is optional for web language support.

After the maintainer has published the AUR package, install and update with an AUR helper:

```sh
yay -S passenger-ide-bin
yay -Syu
```

Plain `pacman -Syu` does not check AUR. Pacman still owns and removes the installed application files. Installing or
upgrading the package does not remove settings under `~/.local/share/Passenger`.

## Move from a manual installation

Close Passenger, build and install the package, and verify `passenger-ide` starts. Update desktop shortcuts and MCP
commands to `passenger-ide` (preserving flags such as `--mcp-attach`). Back up settings before making other changes.

Only then move the old `~/.local/opt/passenger` folder and any version backups to the desktop's Trash. Remove the old
`~/.local/bin/passenger` symlink only if it points to that manual installation. A user-scoped Passenger desktop entry,
icons and MIME definition can shadow the packaged ones; move those exact Passenger files to Trash as well. Do not
remove the settings folder, unrelated files, or a system installation owned by another package.

## Maintainer setup

Publishing is off until the repository variable `AUR_PUBLISH_ENABLED` equals `true`. Other release behavior does not
require an AUR account or key. Before enabling it:

1. Create an AUR account, register its SSH public key, and check that `passenger-ide-bin` is available. If the package
   exists, coordinate with its maintainer instead of replacing their work.
2. Create the GitHub environment `aur`. Restrict it to reviewed stable release tags (`v*`) and require approval.
   Only upstream `Zaneris/passenger` tag releases can enter the publishing job; fork runs and dry runs cannot.
3. Store the matching private key as environment secret `AUR_SSH_PRIVATE_KEY`. The key must be usable without an
   interactive passphrase. Protect it as an AUR account credential, not as a GitHub token.
4. Set environment variable `AUR_KNOWN_HOSTS` to verified `aur.archlinux.org` SSH host-key entries. Compare fingerprints
   against Arch's published AUR SSH fingerprints using a trusted source. Do not trust an unverified `ssh-keyscan` result
   or disable host checking.
5. Set environment variables `AUR_GIT_NAME` and `AUR_GIT_EMAIL` to the public identity for AUR commits. AUR history is
   public and difficult to change later. Enable the repository variable only after these values are ready.

The existing manually dispatched Release workflow builds the Arch package before publishing to GitHub. It generates
`.SRCINFO` with `makepkg` and keeps `PKGBUILD` plus `.SRCINFO` as `Passenger-aur-VERSION`, including during dry runs.
Neither the dry-run validation job nor PRs receive AUR credentials. Do not add an automatic workflow trigger.

After the GitHub release succeeds, the opt-in job pushes only those two small files to
`ssh://aur@aur.archlinux.org/passenger-ide-bin.git`. Its first push can create the AUR package repository. It never
force-pushes, skips an identical retry, and rejects older versions or changed content at the same version. A packaging
revision needs an increased `pkgrel` in the template and regenerated metadata.

## Retry and validation

If only the AUR job fails, use GitHub's **Re-run failed jobs** while the recipe artifact remains available. Do not
recreate the GitHub release. Alternatively, download the retained `Passenger-aur-VERSION` artifact and run the publisher
with a trusted local SSH configuration and an explicitly configured public Git identity:

```sh
python3 tools/publish-aur.py /path/to/recipe /path/to/new-checkout
```

The checkout destination must not already exist. If the artifact has expired, recover the recipe from a trusted copy or
regenerate it from that exact tag and its unchanged release assets, then generate `.SRCINFO` on Arch. Never substitute
`releases/latest`. A concurrent AUR edit makes a normal push fail; inspect and reconcile it rather than force-pushing.

Run the focused checks on Arch as a non-root user:

```sh
python3 tools/test-package-arch.py
```

The tests exercise generation, package layout through real `makepkg`, and publishing against a local bare Git
repository. They do not contact AUR, need credentials, or change an installed Passenger. Release validation also builds
the actual x86_64 package; an aarch64 runtime smoke test still requires an aarch64 machine.

References: [AUR submission guidelines](https://wiki.archlinux.org/title/AUR_submission_guidelines) and
[PKGBUILD](https://wiki.archlinux.org/title/PKGBUILD).

#!/usr/bin/env python3
"""Check the generated Arch package and AUR updates without contacting AUR. Run on Arch as a non-root user."""
import hashlib
import io
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tarfile
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
APP = "io.github.zaneris.passenger"


class ArchPackagingTests(unittest.TestCase):
    def setUp(self):
        results = ROOT / "artifacts/arch-tests"
        results.mkdir(parents=True, exist_ok=True)
        self.folder = Path(tempfile.mkdtemp(prefix="run-", dir=results))
        home = self.folder / "home"
        home.mkdir()
        self.env = os.environ | {
            "HOME": str(home), "XDG_CONFIG_HOME": str(home / ".config"),
            "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1", "GIT_TERMINAL_PROMPT": "0",
            "GIT_AUTHOR_NAME": "Packaging test", "GIT_AUTHOR_EMAIL": "test@example.invalid",
            "GIT_COMMITTER_NAME": "Packaging test", "GIT_COMMITTER_EMAIL": "test@example.invalid",
            "GIT_SSH_COMMAND": "false",
        }
        self.assets = self.folder / "assets"
        self.assets.mkdir()
        self.app = b'#!/bin/sh\nprintf "%s\\n" "$@"\n'
        files = {
            "app/Passenger": self.app,
            "app/Passenger.dll": b"The packaged binary must stay unchanged.\x00\xff",
            "app/LICENSE.txt": (ROOT / "LICENSE").read_bytes(),
            "app/THIRD-PARTY-NOTICES.txt": b"Bundled dependency notices\n",
            "passenger": (ROOT / "tools/linux/passenger").read_bytes(),
            "install.sh": b"#!/bin/sh\nexit 99\n",
            f"{APP}.desktop": (ROOT / f"tools/linux/{APP}.desktop").read_bytes(),
            f"{APP}.xml": (ROOT / f"tools/linux/{APP}.xml").read_bytes(),
            f"{APP}.svg": b'<svg xmlns="http://www.w3.org/2000/svg"/>',
            f"{APP}.png": b"fixture icon",
        }
        for architecture in ("x64", "arm64"):
            with tarfile.open(self.assets / f"Passenger-linux-{architecture}.tar.gz", "w:gz") as archive:
                for name, content in files.items():
                    member = tarfile.TarInfo(f"passenger/{name}")
                    member.size = len(content)
                    member.mode = 0o755 if name in ("passenger", "install.sh", "app/Passenger") else 0o644
                    archive.addfile(member, io.BytesIO(content))

    def command(self, *arguments, cwd=None, succeeds=True):
        result = subprocess.run(arguments, cwd=cwd or self.folder, env=self.env,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        message = f"{arguments} in {cwd or self.folder}\n{result.stdout}\n{result.stderr}"
        if succeeds:
            self.assertEqual(result.returncode, 0, message)
        else:
            self.assertNotEqual(result.returncode, 0, message)
        return result.stdout

    def generate(self, version="0.2.0", name="recipe"):
        recipe = self.folder / name
        self.command(sys.executable, str(ROOT / "tools/package-arch.py"), version, str(self.assets), str(recipe))
        return recipe

    def source_info(self, recipe):
        metadata = self.command("makepkg", "--printsrcinfo", cwd=recipe)
        (recipe / ".SRCINFO").write_text(metadata, encoding="utf-8")
        return metadata

    def publish(self, recipe, name, succeeds=True):
        return self.command(sys.executable, str(ROOT / "tools/publish-aur.py"), str(recipe),
                            str(self.folder / name), "--repository", str(self.folder / "aur.git"), succeeds=succeeds)

    def test_generation_uses_both_versioned_assets_and_is_repeatable(self):
        recipe = self.generate()
        original = (recipe / "PKGBUILD").read_bytes()
        self.generate()
        self.assertEqual((recipe / "PKGBUILD").read_bytes(), original)
        metadata = self.source_info(recipe)
        self.assertIn("depends = ttf-font", metadata)
        for architecture, rid in (("x86_64", "x64"), ("aarch64", "arm64")):
            digest = hashlib.sha256((self.assets / f"Passenger-linux-{rid}.tar.gz").read_bytes()).hexdigest()
            self.assertIn(f"sha256sums_{architecture} = {digest}", metadata)
            self.assertIn(f"Passenger-0.2.0-linux-{rid}.tar.gz::https://github.com/Zaneris/passenger/releases/"
                          f"download/v0.2.0/Passenger-linux-{rid}.tar.gz", metadata)
        self.assertNotIn("@SHA256_", original.decode())
        self.assertNotIn("releases/latest", original.decode())

    def test_generation_rejects_invalid_versions_and_missing_or_unsafe_assets(self):
        output = self.folder / "invalid"
        for version in ("v0.2.0", "0.2", "0.2.0-rc1", "0.2.0;echo unsafe", "../0.2.0"):
            with self.subTest(version=version):
                self.command(sys.executable, str(ROOT / "tools/package-arch.py"), version,
                             str(self.assets), str(output), succeeds=False)
                self.assertFalse(output.exists())
        arm = self.assets / "Passenger-linux-arm64.tar.gz"
        saved = self.assets / "saved.tar.gz"
        arm.rename(saved)
        self.command(sys.executable, str(ROOT / "tools/package-arch.py"), "0.2.0",
                     str(self.assets), str(output), succeeds=False)
        self.assertFalse(output.exists())
        arm.symlink_to(saved)
        self.command(sys.executable, str(ROOT / "tools/package-arch.py"), "0.2.0",
                     str(self.assets), str(output), succeeds=False)
        self.assertFalse(output.exists())
        arm.unlink()
        arm.write_bytes(b"")
        self.command(sys.executable, str(ROOT / "tools/package-arch.py"), "0.2.0",
                     str(self.assets), str(output), succeeds=False)
        self.assertFalse(output.exists())

    def test_makepkg_keeps_binaries_and_registers_the_ide_without_a_manual_installer(self):
        recipe = self.generate()
        for rid in ("x64", "arm64"):
            (recipe / f"Passenger-0.2.0-linux-{rid}.tar.gz").symlink_to(self.assets / f"Passenger-linux-{rid}.tar.gz")
        self.command("makepkg", "--nodeps", "--noconfirm", "--nosign", cwd=recipe)
        packaged = recipe / "pkg/passenger-ide-bin"
        app = packaged / "opt/passenger-ide"
        self.assertEqual((app / "app/Passenger").read_bytes(), self.app)
        self.assertEqual((app / "app/Passenger.dll").read_bytes(), b"The packaged binary must stay unchanged.\x00\xff")
        self.assertTrue(os.access(app / "app/Passenger", os.X_OK))
        self.assertTrue(os.access(app / "passenger", os.X_OK))
        self.assertEqual(os.readlink(packaged / "usr/bin/passenger-ide"), "/opt/passenger-ide/passenger")
        self.assertFalse((packaged / "usr/bin/passenger").exists())
        self.assertFalse((app / "install.sh").exists())
        self.assertFalse((packaged / "home").exists())
        self.assertEqual(self.command(str(app / "passenger"), "--file", "a folder/file.cs"),
                         "--file\na folder/file.cs\n")
        desktop = packaged / f"usr/share/applications/{APP}.desktop"
        self.assertIn("Exec=passenger-ide %f\n", desktop.read_text())
        self.assertIn("TryExec=passenger-ide\n", desktop.read_text())
        self.command("desktop-file-validate", str(desktop))
        for relative in (f"icons/hicolor/256x256/apps/{APP}.png", f"icons/hicolor/scalable/apps/{APP}.svg",
                         f"mime/packages/{APP}.xml", "licenses/passenger-ide-bin/LICENSE.txt",
                         "licenses/passenger-ide-bin/THIRD-PARTY-NOTICES.txt"):
            self.assertTrue((packaged / "usr/share" / relative).is_file(), relative)
        package = self.command("makepkg", "--packagelist", cwd=recipe).strip()
        self.assertTrue(Path(package).is_file(), package)
        self.assertIn("pkgname = passenger-ide-bin", self.command("bsdtar", "-xOf", package, ".PKGINFO"))

    def test_publication_is_idempotent_and_rejects_regressions(self):
        self.command("git", "init", "--bare", "--initial-branch=master", str(self.folder / "aur.git"))
        recipe = self.generate()
        self.source_info(recipe)
        self.publish(recipe, "first")
        repository = self.folder / "aur.git"
        initial = self.command("git", "--git-dir", str(repository), "rev-parse", "master")
        self.assertEqual(self.command("git", "--git-dir", str(repository), "ls-tree", "--name-only", "master"),
                         ".SRCINFO\nPKGBUILD\n")
        self.assertIn("already identical", self.publish(recipe, "retry"))
        self.assertEqual(self.command("git", "--git-dir", str(repository), "rev-parse", "master"), initial)
        with (recipe / "PKGBUILD").open("a") as stream:
            stream.write("\n# same-version change\n")
        self.publish(recipe, "same-version", succeeds=False)
        newer = self.generate("0.3.0", "newer")
        self.source_info(newer)
        self.publish(newer, "upgrade")
        upgraded = self.command("git", "--git-dir", str(repository), "rev-parse", "master")
        self.assertNotEqual(upgraded, initial)
        older = self.generate("0.1.0", "older")
        self.source_info(older)
        self.publish(older, "downgrade", succeeds=False)
        self.assertEqual(self.command("git", "--git-dir", str(repository), "rev-parse", "master"), upgraded)

        tree = self.command("git", "--git-dir", str(repository), "rev-parse", "master^{tree}").strip()
        message = self.folder / "concurrent-message"
        message.write_text("A concurrent maintainer edit\n")
        raced = self.command("git", "--git-dir", str(repository), "commit-tree", tree,
                             "-p", upgraded.strip(), "-F", str(message)).strip()
        hooks = self.folder / "hooks"
        hooks.mkdir()
        hook = hooks / "pre-push"
        hook.write_text(f"#!/bin/sh\ngit --git-dir {shlex.quote(str(repository))} update-ref "
                        f"refs/heads/master {raced} {upgraded.strip()}\n")
        hook.chmod(0o755)
        self.env |= {"GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.hooksPath",
                     "GIT_CONFIG_VALUE_0": str(hooks)}
        latest = self.generate("0.4.0", "latest")
        self.source_info(latest)
        self.publish(latest, "race", succeeds=False)
        self.assertEqual(self.command("git", "--git-dir", str(repository), "rev-parse", "master").strip(), raced)

    def test_publication_rejects_extra_files_and_mismatched_metadata_before_cloning(self):
        recipe = self.generate()
        self.source_info(recipe)
        extra = recipe / "archive.tar.gz"
        extra.write_bytes(b"Not for AUR")
        self.publish(recipe, "extra", succeeds=False)
        self.assertFalse((self.folder / "extra").exists())
        extra.unlink()
        metadata = (recipe / ".SRCINFO").read_text()
        (recipe / ".SRCINFO").write_text(metadata.replace("pkgver = 0.2.0", "pkgver = 9.9.9"))
        self.publish(recipe, "mismatch", succeeds=False)
        self.assertFalse((self.folder / "mismatch").exists())
        saved = self.folder / "saved-srcinfo"
        (recipe / ".SRCINFO").rename(saved)
        (recipe / ".SRCINFO").symlink_to(saved)
        self.publish(recipe, "symlink", succeeds=False)
        self.assertFalse((self.folder / "symlink").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)

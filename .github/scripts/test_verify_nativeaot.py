"""Offline guards for real NativeAOT evidence; synthetic files are not AOT proof."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest

import test_prepare_release


@unittest.skipUnless(sys.platform == "linux", "Linux process ownership regression")
class NativeAotInterruptionTests(unittest.TestCase):
    def check_interruption(self, interrupt):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            source.mkdir()
            for name in ("verify_nativeaot.py", "prepare_release.py"):
                shutil.copyfile(Path(__file__).with_name(name), source / name)
            for command in (["git", "init", "-q"], ["git", "add", "."],
                            ["git", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
                             "commit", "-qm", "Owned interruption fixture"]):
                subprocess.run(command, cwd=source, check=True, capture_output=True)
            commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=source, text=True).strip()
            binary = root / "bin"
            binary.mkdir()
            ready = root / "ready.json"
            child = root / "child.py"
            child.write_text("import json, os, signal, time\nfrom pathlib import Path\n"
                             "signal.signal(signal.SIGTERM, signal.SIG_IGN)\n"
                             "Path(os.environ['TEST_READY']).write_text(json.dumps({'child': os.getpid(), "
                             "'parent': os.getppid(), 'group': os.getpgrp()}))\ntime.sleep(60)\n")
            fake = binary / "dotnet"
            fake.write_text(f"#!{sys.executable}\nimport signal, subprocess, sys\n"
                            "if '--version' in sys.argv:\n print('10.0.401'); sys.exit(0)\n"
                            "signal.signal(signal.SIGTERM, signal.SIG_IGN)\n"
                            f"subprocess.run([sys.executable, {str(child)!r}])\n")
            fake.chmod(0o755)
            env = dict(os.environ, PATH=str(binary) + os.pathsep + os.defpath, TEST_READY=str(ready),
                       PYTHONDONTWRITEBYTECODE="1")
            output = root / "evidence"
            command = [sys.executable, str(source / "verify_nativeaot.py"), "--source", str(source),
                       "--commit", commit, "--output", str(output), "--sdk", "10.0.401",
                       "--tfm", "net10.0", "--runtime", "10.0.12", "--rid", "linux-x64"]
            owned = None
            with (root / "outer.log").open("w") as log:
                process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT, env=env,
                                           start_new_session=True)
                try:
                    deadline = time.monotonic() + 5
                    while not ready.exists() and process.poll() is None and time.monotonic() < deadline:
                        time.sleep(0.02)
                    self.assertTrue(ready.exists(), (root / "outer.log").read_text())
                    owned = json.loads(ready.read_text())
                    process.send_signal(interrupt)
                    code = process.wait(timeout=12)
                    deadline = time.monotonic() + 2
                    def alive(pid):
                        status = Path(f"/proc/{pid}/stat")
                        return status.exists() and status.read_text().split(") ", 1)[1].split()[0] != "Z"
                    while any(alive(owned[key]) for key in ("parent", "child")) and time.monotonic() < deadline:
                        time.sleep(0.02)
                    live = [key for key in ("parent", "child") if alive(owned[key])]
                    print(f"interrupt={interrupt.name} outer_exit={code} live_owned={live}", flush=True)
                    self.assertNotEqual(0, code)
                    self.assertEqual([], live, "Interruption left owned subprocesses alive")
                    manifest = json.loads((output / "nativeaot-manifest.json").read_text())
                    self.assertEqual("failed", manifest["status"])
                    self.assertIsNotNone(manifest["commands"][0]["exit_code"])
                finally:
                    if owned:
                        try:
                            os.killpg(owned["group"], signal.SIGKILL)
                        except ProcessLookupError:
                            pass
                    if process.poll() is None:
                        process.kill()
                    process.wait(timeout=5)

    def test_keyboard_interrupt_cleans_owned_processes_and_records_failure(self):
        self.check_interruption(signal.SIGINT)

    def test_termination_cleans_owned_processes_and_records_failure(self):
        self.check_interruption(signal.SIGTERM)


class NativeAotEvidenceTests(unittest.TestCase):
    def setUp(self):
        script = Path(__file__).with_name("verify_nativeaot.py")
        self.assertTrue(script.is_file(), "A reusable package-to-native execution verifier is required")
        spec = importlib.util.spec_from_file_location("verify_nativeaot", script)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.fixture = test_prepare_release.ReleaseIdentityTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.root = self.fixture.root

    def inventory(self):
        self.fixture.inventory()
        for ext in ("nupkg", "snupkg"):
            self.fixture.package("HeroMessaging.SourceGenerators", extension=ext)

    def test_four_exact_packages_are_required(self):
        self.inventory()
        self.assertEqual(8, len(self.module.verify_packages(self.root, self.fixture.version, self.fixture.commit)))
        (self.root / f"HeroMessaging.SourceGenerators.{self.fixture.version}.nupkg").unlink()
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.module.verify_packages(self.root, self.fixture.version, self.fixture.commit)

    def test_aligned_identity_cannot_hide_wrong_internal_dependency(self):
        self.inventory()
        self.fixture.package("HeroMessaging", dependencies={"HeroMessaging.Abstractions": "0.0.0"})
        with self.assertRaisesRegex(ValueError, "dependenc"):
            self.module.verify_packages(self.root, self.fixture.version, self.fixture.commit)

    def test_consumer_install_must_match_built_package_bytes(self):
        self.inventory()
        packages = self.module.verify_packages(self.root, self.fixture.version, self.fixture.commit)
        installed = self.root / "installed"
        for package in packages:
            if package["extension"] == ".nupkg":
                target = installed / package["id"].lower() / package["version"] / package["file"].lower()
                target.parent.mkdir(parents=True)
                target.write_bytes((self.root / package["file"]).read_bytes())
        self.assertEqual(4, len(self.module.verify_installed(packages, installed)))
        target.write_bytes(b"different cache content")
        with self.assertRaisesRegex(ValueError, "installed|Installed"):
            self.module.verify_installed(packages, installed)

    def test_target_must_be_explicit_matching_stable_versions(self):
        self.module.validate_target("10.0.401", "net10.0", "10.0.12", "linux-x64")
        self.module.validate_target("11.0.100", "net11.0", "11.0.0", "linux-x64")
        for values in [("10.0.x", "net10.0", "10.0.12", "linux-x64"),
                       ("10.0.401", "net11.0", "11.0.0", "linux-x64"),
                       ("10.0.401", "net10.0", "10.0.12", "win-x64")]:
            with self.subTest(values=values), self.assertRaises(ValueError):
                self.module.validate_target(*values)

    def test_compile_only_or_non_native_or_failed_execution_cannot_pass(self):
        executable = self.root / "consumer"
        header = bytearray(20)
        header[:6] = b"\x7fELF\x02\x01"
        header[18:20] = (62).to_bytes(2, "little")
        executable.write_bytes(header)
        self.module.verify_execution(executable, 0, "NATIVE_AOT_PASS\n")
        for code, output in [(0, "Build succeeded."), (1, "NATIVE_AOT_PASS\n"), (0, "")]:
            with self.subTest(code=code, output=output), self.assertRaises(ValueError):
                self.module.verify_execution(executable, code, output)
        executable.write_bytes(b"MZ managed output")
        with self.assertRaisesRegex(ValueError, "ELF"):
            self.module.verify_execution(executable, 0, "NATIVE_AOT_PASS\n")


if __name__ == "__main__":
    unittest.main()

"""Offline guards for real NativeAOT evidence; synthetic files are not AOT proof."""
import importlib.util
from pathlib import Path
import unittest

import test_prepare_release


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

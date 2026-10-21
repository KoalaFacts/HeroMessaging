"""Offline package identity regressions; never builds, signs or publishes."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zipfile


SCRIPT = Path(__file__).with_name("prepare_release.py")


class ReleaseIdentityTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SCRIPT.is_file(), "Release preparation must validate package contents, not rename CI archives")
        spec = importlib.util.spec_from_file_location("prepare_release", SCRIPT)
        self.module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.module)
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.commit = "a" * 40
        self.version = "1.2.3-rc.2"

    def package(self, package_id, version=None, commit=None, dependencies=None, extension="nupkg", name=None):
        version = version or self.version
        document = ET.Element("package", xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd")
        metadata = ET.SubElement(document, "metadata")
        ET.SubElement(metadata, "id").text = package_id
        ET.SubElement(metadata, "version").text = version
        ET.SubElement(metadata, "repository", type="git", url="https://github.com/KoalaFacts/HeroMessaging", commit=commit or self.commit)
        groups = ET.SubElement(metadata, "dependencies")
        for framework in ("net8.0", "net9.0", "net10.0"):
            group = ET.SubElement(groups, "group", targetFramework=framework)
            for dep_id, dep_version in (dependencies or {}).items():
                ET.SubElement(group, "dependency", id=dep_id, version=dep_version)
        path = self.root / (name or f"{package_id}.{version}.{extension}")
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(package_id + ".nuspec", ET.tostring(document))
        return path

    def inventory(self, version=None, commit=None):
        version = version or self.version
        for package_id in self.module.RELEASE_IDS:
            deps = {"Microsoft.Extensions.Logging.Abstractions": "10.0.13"}
            if package_id == "HeroMessaging":
                deps.update({"HeroMessaging.Abstractions": version, "HeroMessaging.RingBuffer": version})
            for extension in ("nupkg", "snupkg"):
                self.package(package_id, version, commit, deps, extension)

    def verify(self):
        return self.module.verify_release(self.root, self.version, self.commit)

    def test_matching_release_and_internal_dependencies_pass(self):
        self.inventory()
        self.assertEqual(len(self.verify()), 6)

    def test_rename_cannot_change_internal_version(self):
        self.inventory()
        self.package("HeroMessaging", "0.0.0-ci.main-20261021.1.abcdef0", name=f"HeroMessaging.{self.version}.nupkg")
        with self.assertRaisesRegex(ValueError, "version"):
            self.verify()

    def test_wrong_source_commit_fails(self):
        self.inventory(commit="b" * 40)
        with self.assertRaisesRegex(ValueError, "commit"):
            self.verify()

    def test_filename_must_match_internal_identity(self):
        self.inventory()
        self.package("Another.Package", name=f"HeroMessaging.{self.version}.nupkg")
        with self.assertRaisesRegex(ValueError, "identity|inventory"):
            self.verify()

    def test_internal_dependency_must_use_requested_version(self):
        self.inventory()
        self.package("HeroMessaging", dependencies={"HeroMessaging.Abstractions": "0.0.0"})
        with self.assertRaisesRegex(ValueError, "dependency"):
            self.verify()

    def test_missing_ring_buffer_package_fails(self):
        self.inventory()
        (self.root / f"HeroMessaging.RingBuffer.{self.version}.nupkg").unlink()
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.verify()

    def test_missing_internal_dependency_fails(self):
        self.inventory()
        self.package("HeroMessaging", dependencies={"HeroMessaging.Abstractions": self.version})
        with self.assertRaisesRegex(ValueError, "dependency"):
            self.verify()

    def test_missing_symbols_fail(self):
        self.inventory()
        (self.root / f"HeroMessaging.{self.version}.snupkg").unlink()
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.verify()

    def test_unexpected_package_fails(self):
        self.inventory()
        self.package("Another.Package")
        with self.assertRaisesRegex(ValueError, "inventory"):
            self.verify()

    def test_ci_packages_are_bound_to_commit_and_identical_ci_version(self):
        version = "0.0.0-ci.main-20261021.1.abcdef0"
        for package_id in self.module.CI_IDS:
            for ext in ("nupkg", "snupkg"):
                self.package(package_id, version, extension=ext)
        self.assertEqual(len(self.module.verify_ci(self.root, self.commit)), 4)
        self.package("HeroMessaging", version, commit="b" * 40)
        with self.assertRaisesRegex(ValueError, "commit"):
            self.module.verify_ci(self.root, self.commit)

    def test_ci_mixed_versions_fail(self):
        for package_id in self.module.CI_IDS:
            for ext in ("nupkg", "snupkg"):
                self.package(package_id, f"0.0.0-ci.{package_id}", extension=ext)
        with self.assertRaisesRegex(ValueError, "version"):
            self.module.verify_ci(self.root, self.commit)

    def test_external_dependency_changes_fail(self):
        self.inventory()
        packages = self.verify()
        ci = [dict(p) for p in packages if p["id"] in self.module.CI_IDS]
        ci[0] = dict(ci[0], dependencies={"net8.0": {"Microsoft.Extensions.Logging.Abstractions": "9.0.0"}})
        with self.assertRaisesRegex(ValueError, "external dependenc"):
            self.module.verify_external_dependencies(ci, packages)

    def test_noncanonical_version_is_rejected(self):
        for version in ("01.2.3", "1.2.3-01", "1.2.3+metadata", "1.2.3;echo oops", ""):
            with self.subTest(version=version), self.assertRaises(ValueError):
                self.module.validate_version(version)


if __name__ == "__main__":
    unittest.main()

"""Build release packages from the exact source validated by a successful CI run.

This command only prepares local artifacts. It never signs, tags or publishes.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile


CI_IDS = {"HeroMessaging", "HeroMessaging.Abstractions"}
RELEASE_IDS = CI_IDS | {"HeroMessaging.RingBuffer"}


def validate_version(version):
    number = r"(?:0|[1-9][0-9]*)"
    if not re.fullmatch(rf"{number}\.{number}\.{number}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?", version):
        raise ValueError("Release version must be canonical X.Y.Z[-prerelease], without build metadata")
    for identifier in version.partition("-")[2].split("."):
        if identifier.isdigit() and len(identifier) > 1 and identifier[0] == "0":
            raise ValueError("Numeric prerelease identifiers cannot have leading zeroes")


def read_packages(directory, commit, expected_ids):
    packages = []
    for path in sorted(directory.iterdir()):
        if path.suffix not in (".nupkg", ".snupkg"):
            continue
        with zipfile.ZipFile(path) as archive:
            if archive.testzip():
                raise ValueError(f"Corrupt archive: {path.name}")
            names = [name for name in archive.namelist() if name.endswith(".nuspec")]
            if len(names) != 1:
                raise ValueError(f"Expected one nuspec: {path.name}")
            root = ET.fromstring(archive.read(names[0]))
        # NuGet uses different schema versions; compare local element names.
        for element in root.iter():
            element.tag = element.tag.rsplit("}", 1)[-1]
        metadata = root.find("metadata")
        if metadata is None:
            raise ValueError(f"Missing package metadata: {path.name}")
        package_id, version = metadata.findtext("id"), metadata.findtext("version")
        if path.name != f"{package_id}.{version}{path.suffix}":
            raise ValueError(f"Filename identity/version differs from nuspec: {path.name}")
        repository = metadata.find("repository")
        if repository is None or repository.get("commit") != commit:
            raise ValueError(f"Source commit mismatch: {path.name}")
        dependencies = {}
        for group in metadata.findall("dependencies/group"):
            dependencies[group.get("targetFramework", "")] = {d.get("id"): d.get("version") for d in group.findall("dependency")}
        packages.append({"file": path.name, "id": package_id, "version": version,
                         "extension": path.suffix, "commit": commit, "dependencies": dependencies,
                         "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    actual = [(p["id"], p["extension"]) for p in packages]
    expected = {(package_id, ext) for package_id in expected_ids for ext in (".nupkg", ".snupkg")}
    if len(actual) != len(expected) or set(actual) != expected:
        raise ValueError(f"Package inventory mismatch: expected {sorted(expected)}, got {actual}")
    return packages


def verify_ci(directory, commit):
    packages = read_packages(directory, commit, CI_IDS)
    versions = {p["version"] for p in packages}
    if len(versions) != 1 or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+-ci\.[0-9A-Za-z.-]+", next(iter(versions))):
        raise ValueError("CI packages must share one CI version")
    return packages


def verify_release(directory, version, commit):
    packages = read_packages(directory, commit, RELEASE_IDS)
    for package in packages:
        if package["version"] != version:
            raise ValueError(f"Release version mismatch: {package['file']}")
        for dependencies in package["dependencies"].values():
            internal = {name: value for name, value in dependencies.items() if name.startswith("HeroMessaging")}
            expected = {name: version for name in RELEASE_IDS - {"HeroMessaging"}} if package["id"] == "HeroMessaging" else {}
            if internal != expected:
                raise ValueError(f"Internal dependency versions do not close over release packages: {package['file']}")
        if package["id"] == "HeroMessaging" and not package["dependencies"]:
            raise ValueError("Missing internal dependency groups")
    return packages


def verify_external_dependencies(ci_packages, release_packages):
    def external(package):
        return {framework: {name: version for name, version in dependencies.items() if not name.startswith("HeroMessaging")}
                for framework, dependencies in package["dependencies"].items()}
    by_identity = {(p["id"], p["extension"]): p for p in release_packages}
    for package in ci_packages:
        if external(package) != external(by_identity[package["id"], package["extension"]]):
            raise ValueError(f"CI external dependencies changed: {package['file']}")


def prepare(args):
    validate_version(args.version)
    if not re.fullmatch(r"[a-f0-9]{40}", args.commit) or not re.fullmatch(r"[1-9][0-9]*", args.ci_run_id):
        raise ValueError("An exact source commit and CI run ID are required")
    source = args.source.resolve()
    actual = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=source, text=True).strip()
    if actual != args.commit:
        raise ValueError(f"Checked out source commit {actual} does not match {args.commit}")
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=normal"], cwd=source, text=True).strip():
        raise ValueError("Release source checkout must be clean")
    ci_packages = verify_ci(args.ci_packages.resolve(), args.commit)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    for package_id in sorted(RELEASE_IDS):
        subprocess.run([
            "dotnet", "pack", str(source / "src" / package_id / f"{package_id}.csproj"),
            "--configuration", "Release", "--output", str(output), "--disable-build-servers",
            "-nodeReuse:false", "-p:UseSharedCompilation=false", "-warnaserror",
            f"-p:Version={args.version}", f"-p:PackageVersion={args.version}",
            f"-p:RepositoryCommit={args.commit}", "-p:ContinuousIntegrationBuild=true",
            "-p:IncludeSymbols=true", "-p:SymbolPackageFormat=snupkg",
        ], cwd=source, check=True)
    release_packages = verify_release(output, args.version, args.commit)
    verify_external_dependencies(ci_packages, release_packages)
    manifest = {"source_commit": args.commit, "release_version": args.version,
                "validated_ci_run_id": args.ci_run_id, "ci_artifact": f"release-packages-{args.commit}",
                "ci_packages": ci_packages, "unsigned_release_packages": release_packages}
    (output / "release-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Verified {len(release_packages)} release archives at {args.version} from {args.commit}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--ci-packages", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--ci-run-id", required=True)
    prepare(parser.parse_args())

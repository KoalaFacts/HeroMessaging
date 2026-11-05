"""Pack a clean source commit and execute the bounded NativeAOT package consumer.

No SDK installation, external service, performance measurement or publication.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import subprocess
import xml.etree.ElementTree as ET

from prepare_release import read_packages


PACKAGE_IDS = {"HeroMessaging", "HeroMessaging.Abstractions", "HeroMessaging.RingBuffer", "HeroMessaging.SourceGenerators"}


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def materialize_consumer(source, target):
    shutil.copytree(source, target, ignore=shutil.ignore_patterns("bin", "obj"))
    (target / "NativeAotConsumer.csproj.template").rename(target / "NativeAotConsumer.csproj")


def validate_target(sdk, tfm, runtime, rid):
    if not all(re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", value) for value in (sdk, runtime)):
        raise ValueError("Exact stable SDK and runtime versions are required")
    family = ".".join(runtime.split(".")[:2])
    if sdk.split(".")[:2] != runtime.split(".")[:2] or tfm != f"net{family}" or int(runtime.split(".")[0]) < 10:
        raise ValueError("SDK, TFM and runtime must match a .NET 10 or later family")
    if rid != "linux-x64":
        raise ValueError("This consumer currently verifies linux-x64 only")


def verify_packages(directory, version, commit):
    packages = read_packages(directory, commit, PACKAGE_IDS)
    for package in packages:
        if package["version"] != version:
            raise ValueError("Consumer package version mismatch")
        expected = {name: version for name in ("HeroMessaging.Abstractions", "HeroMessaging.RingBuffer")} if package["id"] == "HeroMessaging" else {}
        if expected and not package["dependencies"]:
            raise ValueError("Missing internal dependency groups")
        for group in package["dependencies"].values():
            if {name: value for name, value in group.items() if name.startswith("HeroMessaging")} != expected:
                raise ValueError("Consumer internal dependency versions do not match")
    return packages


def verify_installed(packages, installed):
    identities = []
    for package in packages:
        if package["extension"] != ".nupkg":
            continue
        path = installed / package["id"].lower() / package["version"] / package["file"].lower()
        if not path.is_file() or sha256(path) != package["sha256"]:
            raise ValueError(f"Installed package does not match built bytes: {package['id']}")
        identities.append({"id": package["id"], "version": package["version"], "sha256": sha256(path)})
    return identities


def verify_execution(executable, code, output):
    header = executable.read_bytes()[:20]
    if len(header) < 20 or header[:6] != b"\x7fELF\x02\x01" or int.from_bytes(header[18:20], "little") != 62:
        raise ValueError("Expected a Linux x64 ELF, not managed build output")
    if code != 0 or "NATIVE_AOT_PASS" not in output.splitlines():
        raise ValueError("NativeAOT execution and all consumer assertions must succeed")


def verify(args):
    validate_target(args.sdk, args.tfm, args.runtime, args.rid)
    if platform.system() != "Linux" or platform.machine() != "x86_64":
        raise ValueError("Run on the existing Linux x64 host; no cross compilation")
    source = args.source.resolve()
    git = lambda *values: subprocess.check_output(["git", *values], cwd=source, text=True).strip()
    if Path(git("rev-parse", "--show-toplevel")).resolve() != source:
        raise ValueError("Source must be a complete checkout")
    commit = git("rev-parse", "HEAD")
    if not re.fullmatch(r"[a-f0-9]{40}", args.commit) or commit != args.commit or git("status", "--porcelain", "--untracked-files=normal"):
        raise ValueError("An exact clean source commit is required")
    actual_sdk = subprocess.check_output(["dotnet", "--version"], cwd=source, text=True).strip()
    if actual_sdk != args.sdk:
        raise ValueError(f"Selected SDK {actual_sdk} differs from requested {args.sdk}; no SDK is installed by this script")
    if not shutil.which("timeout"):
        raise ValueError("GNU timeout is required to bound owned build and consumer commands")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    manifest = {"status": "running", "source_commit": commit, "sdk": actual_sdk, "tfm": args.tfm,
                "runtime": args.runtime, "rid": args.rid, "os": platform.platform(), "commands": []}
    manifest_path = output / "nativeaot-manifest.json"

    def save():
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")

    def run(name, argv, cwd, seconds=300, env=None, check=True):
        command = ["timeout", "--kill-after=5s", f"{seconds}s", *argv]
        log = output / f"{name}.log"
        manifest["commands"].append({"argv": command, "cwd": str(cwd), "log": log.name, "exit_code": None})
        save()
        with log.open("w") as stream:
            process = subprocess.Popen(command, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT,
                                       env=env, start_new_session=True)
            try:
                code = process.wait()
            finally:
                # Own the whole command group, including children that outlive timeout.
                # These are disposable build/probe commands, with partial evidence retained.
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
                manifest["commands"][-1]["exit_code"] = process.returncode
                save()
        print(f"{name}: exit {code}", flush=True)
        if check and code:
            raise RuntimeError(f"{name} failed; see {log}")
        return code, log.read_text()

    try:
        save()
        run("dotnet-info", ["dotnet", "--info"], source)
        version = f"0.0.0-aot.g{commit}"
        packages = output / "packages"
        packages.mkdir()
        for package_id in sorted(PACKAGE_IDS):
            run(f"pack-{package_id}", ["dotnet", "pack", str(source / "src" / package_id / f"{package_id}.csproj"),
                "-c", "Release", "-o", str(packages), "--disable-build-servers", "-nodeReuse:false",
                "-p:UseSharedCompilation=false", "-warnaserror", f"-p:Version={version}", f"-p:PackageVersion={version}",
                f"-p:RepositoryCommit={commit}", f"-p:SourceRevisionId={commit}", "-p:ContinuousIntegrationBuild=true",
                "-p:IncludeSymbols=true", "-p:SymbolPackageFormat=snupkg"], source)
        manifest["packages"] = verify_packages(packages, version, commit)
        consumer = output / "consumer"
        template = source / "tests" / "NativeAotConsumer" / "NativeAotConsumer.csproj.template"
        manifest["consumer_project_template"] = {"path": str(template.relative_to(source)), "sha256": sha256(template)}
        materialize_consumer(template.parent, consumer)
        (consumer / "global.json").write_text(json.dumps({"sdk": {"version": args.sdk, "rollForward": "disable"}}))
        config = ET.Element("configuration")
        feeds = ET.SubElement(config, "packageSources")
        ET.SubElement(feeds, "clear")
        ET.SubElement(feeds, "add", key="local", value=str(packages))
        ET.SubElement(feeds, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        mappings = ET.SubElement(config, "packageSourceMapping")
        for name, pattern in [("local", "HeroMessaging*"), ("nuget.org", "*")]:
            ET.SubElement(ET.SubElement(mappings, "packageSource", key=name), "package", pattern=pattern)
        ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        installed = output / "installed"
        properties = [f"-p:ConsumerPackageVersion={version}", f"-p:ConsumerTargetFramework={args.tfm}",
                      f"-p:RuntimeFrameworkVersion={args.runtime}", f"-p:RuntimeIdentifier={args.rid}"]
        run("restore", ["dotnet", "restore", "NativeAotConsumer.csproj", "--configfile", "NuGet.Config",
                        "--packages", str(installed), *properties], consumer)
        manifest["installed_packages"] = verify_installed(manifest["packages"], installed)
        manifest["resolved_archives"] = [{"id": p.parent.parent.name, "version": p.parent.name, "sha256": sha256(p)}
                                         for p in sorted(installed.glob("*/*/*.nupkg"))]
        native = output / "native"
        run("publish", ["dotnet", "publish", "NativeAotConsumer.csproj", "-c", "Release", "--no-restore",
                        "--disable-build-servers", "-nodeReuse:false", "-p:UseSharedCompilation=false",
                        "-warnaserror", "-v:normal", "-o", str(native), *properties], consumer)
        executable = native / "NativeAotConsumer"
        code, text = run("execute", [str(executable), f"{version}+{commit}", args.runtime], output, seconds=30,
                         env={"PATH": os.defpath, "LANG": "C.UTF-8"}, check=False)
        verify_execution(executable, code, text)
        manifest["native"] = {"sha256": sha256(executable), "bytes": executable.stat().st_size, "exit_code": code}
        manifest["consumer_inputs"] = {p.name: sha256(p) for p in consumer.iterdir() if p.is_file()}
        manifest["generated_sources"] = {str(p.relative_to(consumer)): sha256(p) for p in sorted((consumer / "obj" / "generated").rglob("*.cs"))}
        if git("status", "--porcelain", "--untracked-files=normal") or git("rev-parse", "HEAD") != commit:
            raise ValueError("Source changed during validation")
        manifest["status"] = "passed"
    except BaseException as error:
        manifest["status"] = "failed"
        manifest["error"] = str(error)
        raise
    finally:
        save()
    print(f"NativeAOT consumer passed for {commit}; evidence: {output}")


if __name__ == "__main__":
    def terminate(signum, frame):
        raise SystemExit(128 + signum)

    signal.signal(signal.SIGTERM, terminate)
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("source", "output"):
        parser.add_argument(f"--{name}", type=Path, required=True)
    for name in ("commit", "sdk", "tfm", "runtime", "rid"):
        parser.add_argument(f"--{name}", required=True)
    verify(parser.parse_args())

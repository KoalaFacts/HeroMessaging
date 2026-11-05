"""Exercise CI shell routing with a recording CLI; no .NET tests or services run."""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]
CI = ROOT / ".github/workflows/ci.yml"


def step_script(name):
    text = CI.read_text()
    step = text.split(f"      - name: {name}\n", 1)[1].split("\n      - ", 1)[0]
    script = step.split("        run: |\n", 1)[1]
    return "\n".join(line[10:] for line in script.splitlines())


class MtpHangContractTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def run_unit_step(self, os_name, framework, failure=0):
        for name in ("HeroMessaging.Tests", "HeroMessaging.Architecture.Tests", "Other.Tests"):
            directory = self.root / "tests" / name
            directory.mkdir(parents=True, exist_ok=True)
            (directory / f"{name}.csproj").touch()
        log = self.root / f"calls-{os_name}-{framework}-{failure}.jsonl"
        script = step_script("Run unit tests")
        script = script.replace("${{ matrix.os }}", os_name).replace("${{ matrix.framework }}", framework)
        prelude = '''dotnet() {
  if [[ "$1" == msbuild ]]; then
    printf 'net8.0;net9.0;net10.0\\n'
  else
    python3 -c 'import json,os,sys; open(os.environ["CALLS"],"a").write(json.dumps(sys.argv[1:])+"\\n")' "$@"
    return "$FAILURE"
  fi
}
'''
        result = subprocess.run(["bash", "-c", prelude + script], cwd=self.root,
                                env={**os.environ, "CALLS": str(log), "FAILURE": str(failure)},
                                capture_output=True, text=True, timeout=15)
        calls = [json.loads(line) for line in log.read_text().splitlines()] if log.exists() else []
        return result, calls

    def test_only_linux_net10_core_gets_mtp_hang_diagnostics(self):
        for os_name in ("ubuntu-latest", "windows-latest", "macos-latest"):
            for framework in ("net8.0", "net9.0", "net10.0"):
                with self.subTest(os=os_name, framework=framework):
                    result, calls = self.run_unit_step(os_name, framework)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    self.assertCountEqual(
                        [Path(args[args.index("--project") + 1]).stem for args in calls],
                        ["HeroMessaging.Tests", "HeroMessaging.Architecture.Tests", "Other.Tests"],
                    )
                    for args in calls:
                        project = args[args.index("--project") + 1]
                        core = project.endswith("/HeroMessaging.Tests.csproj")
                        diagnostic = core and os_name == "ubuntu-latest" and framework == "net10.0"
                        self.assertEqual("--hangdump" in args, diagnostic, args)
                        self.assertEqual("--diagnostic" in args, diagnostic, args)
                        self.assertFalse(any(arg.startswith("--blame") or arg == "--diag" for arg in args), args)
                        self.assertEqual(args[args.index("--filter-not-trait") + 1], "Category=Integration")
                        self.assertEqual(args[args.index("--minimum-expected-tests") + 1], "1")
                        self.assertIn("--report-trx", args)
                        self.assertEqual("--coverlet" in args, "Architecture" not in project)
                        if diagnostic:
                            self.assertEqual(args[args.index("--hangdump-timeout") + 1], "10m")
                            self.assertEqual(args[args.index("--hangdump-type") + 1], "Mini")
                            self.assertEqual(args[args.index("--diagnostic-output-directory") + 1],
                                             "./test-results/HeroMessaging.Tests")

    def test_test_failure_stops_the_loop_and_propagates_exit_code(self):
        result, calls = self.run_unit_step("ubuntu-latest", "net10.0", failure=37)
        self.assertEqual(result.returncode, 37)
        self.assertEqual(len(calls), 1)

    def test_core_has_private_hangdump_extension_without_changing_xunit_entrypoint(self):
        project = ET.parse(ROOT / "tests/HeroMessaging.Tests/HeroMessaging.Tests.csproj")
        reference = project.find(".//PackageReference[@Include='Microsoft.Testing.Extensions.HangDump']")
        self.assertIsNotNone(reference)
        self.assertEqual(reference.get("Version"), "2.5.1")
        self.assertEqual(reference.get("PrivateAssets"), "all")
        props = ET.parse(ROOT / "tests/Directory.Build.props")
        self.assertEqual(props.findtext(".//GenerateTestingPlatformEntryPoint"), "false")

    def test_diagnostic_files_are_retained_even_on_failure(self):
        step = CI.read_text().split("      - name: Upload test results\n", 1)[1].split("\n  integration-tests:", 1)[0]
        self.assertIn("if: always()", step)
        for extension in ("trx", "xml", "diag", "log", "dmp"):
            self.assertIn(f"./test-results/**/*.{extension}", step)

    def test_coverage_search_supports_mtp_suffixes_and_preserves_find_errors(self):
        script = step_script("Generate combined coverage report")
        match = re.search(r"^coverage_file=.+$", script, re.MULTILINE)
        self.assertIsNotNone(match)
        command = "set -euo pipefail\n" + match.group() + '\n[[ -n "$coverage_file" ]]'
        # Missing directory must fail rather than become an empty successful search.
        missing = subprocess.run(["bash", "-c", command], cwd=self.root, capture_output=True, timeout=10)
        self.assertNotEqual(missing.returncode, 0)
        directory = self.root / "all-test-results" / "project with spaces"
        directory.mkdir(parents=True)
        for index in range(200):
            (directory / f"coverage.cobertura.{index}.xml").touch()
        found = subprocess.run(["bash", "-c", command], cwd=self.root, capture_output=True, timeout=10)
        self.assertEqual(found.returncode, 0, found.stderr)
        self.assertEqual(found.stderr, b"")
        # A partial result must not hide an error from the filesystem traversal.
        partial = subprocess.run(
            ["bash", "-c", 'find() { printf "./all-test-results/coverage.cobertura.xml\\n"; return 23; }\n' + command],
            cwd=self.root, capture_output=True, timeout=10,
        )
        self.assertEqual(partial.returncode, 23)

    def test_native_evidence_keeps_marker_paths_and_thirty_days(self):
        text = (ROOT / ".github/workflows/profile-inprocess.yml").read_text()
        self.assertIn("./marker-boundary-results/", text)
        self.assertIn("./marker-boundary-binaries/*.pdb", text)
        self.assertIn("retention-days: 30", text)
        self.assertNotIn("retention-days: 7", text)


if __name__ == "__main__":
    unittest.main()

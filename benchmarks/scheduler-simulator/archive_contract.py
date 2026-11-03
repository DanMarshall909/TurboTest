"""Slow lane: reproduce the frozen reference from the published archive, offline."""

import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest


HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]


class PublishedArchiveContract(unittest.TestCase):
    def test_archived_inputs_reproduce_the_negative_gate_without_docker(self):
        pins = json.loads((HERE / "inputs.json").read_text())
        archive = REPO / "docs/issues/evidence/2026-11-02/tt038/raw-evidence.tar.gz"
        self.assertEqual(pins["archiveFiles"][archive.name], hashlib.sha256(archive.read_bytes()).hexdigest())
        with tempfile.TemporaryDirectory(prefix="tt039-archive-") as temp:
            root = Path(temp) / "v4"
            # Read only pinned files; never extract archive paths or native binaries.
            with tarfile.open(archive) as source:
                for name in pins["files"]:
                    target = root / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(source.extractfile("v4/" + name).read())
            fitted, evaluation = Path(temp) / "fit.json", Path(temp) / "evaluation.json"
            common = [sys.executable, str(HERE / "reference.py")]
            def run(*args):
                return subprocess.run(common + list(args), text=True, capture_output=True,
                                      cwd=REPO, timeout=60)
            result = run("fit", "--trace-root", str(root), "--output", str(fitted))
            self.assertEqual(0, result.returncode, result.stderr)
            original = fitted.read_bytes()
            duplicate = run("fit", "--trace-root", str(root), "--output", str(fitted))
            self.assertEqual(1, duplicate.returncode)
            self.assertIn("ReferenceInput:", duplicate.stderr)
            self.assertEqual(original, fitted.read_bytes())
            missing_fit = run("validate", "--trace-root", str(root), "--output", str(evaluation))
            self.assertNotEqual(0, missing_fit.returncode)
            self.assertIn("validate requires --fit", missing_fit.stderr)
            self.assertFalse(evaluation.exists())
            result = run("validate", "--trace-root", str(root), "--fit", str(fitted),
                         "--output", str(evaluation))
            self.assertEqual(2, result.returncode, result.stderr)
            self.assertFalse(json.loads(result.stdout)["qualified"])
            report = json.loads(evaluation.read_text())
            self.assertEqual((40, 16, 8), (len(report["runs"]), len(report["gate"]["perRunSuite"]),
                                           len(report["gate"]["strata"])))
            failures = {(s["mode"], s["waitMilliseconds"], key) for s in report["gate"]["strata"]
                        for key, metric in s["metrics"].items() if not metric["passed"]}
            self.assertEqual({("awaited", 0, "suite_ms")}, failures)
            self.assertEqual(["r3-w0-awaited"], [r["name"] for r in report["gate"]["perRunSuite"] if not r["passed"]])


if __name__ == "__main__":
    unittest.main()

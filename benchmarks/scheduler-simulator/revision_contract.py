"""Slow CLI boundary lane; independent small trace oracle lives in test_revised."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from test_revised import training_fixture, trace_fixture


class ProspectiveCliContract(unittest.TestCase):
    def test_commands_preserve_outputs_and_cannot_validate_without_a_frozen_forecast(self):
        executable = Path(__file__).with_name("revised.py")
        with tempfile.TemporaryDirectory(prefix="tt039-r2-cli-") as temp:
            root = Path(temp)
            contract, write = training_fixture(root)
            for row in contract["cases"]:
                if row["split"] == "validation":
                    write(row["name"] + "/report.json", trace_fixture(row["mode"]))
                    write(row["name"] + "/process.json", dict(exitCode=0, processGroupStopped=True,
                        deadlineExceeded=False, elapsedSeconds=1))
            def run(action, output, *extra):
                return subprocess.run([sys.executable, str(executable), action, "--trace-root", str(root),
                    "--output", str(root/output), *extra], capture_output=True, text=True, timeout=30)
            missing = run("validate", "evaluation.json")
            self.assertNotEqual(0, missing.returncode)
            self.assertIn("requires --fit and --forecast", missing.stderr)
            self.assertFalse((root/"evaluation.json").exists())
            fitted = run("fit", "fit.json")
            self.assertEqual(0, fitted.returncode, fitted.stderr)
            fit_bytes = (root/"fit.json").read_bytes()
            duplicate = run("fit", "fit.json")
            self.assertEqual(1, duplicate.returncode)
            self.assertIn("RevisedInput:", duplicate.stderr)
            self.assertEqual(fit_bytes, (root/"fit.json").read_bytes())
            forecast = run("forecast", "forecast.json", "--fit", str(root/"fit.json"))
            self.assertEqual(0, forecast.returncode, forecast.stderr)
            result = run("validate", "evaluation.json", "--fit", str(root/"fit.json"),
                         "--forecast", str(root/"forecast.json"))
            self.assertIn(result.returncode, (0, 2), result.stderr)
            evaluation = json.loads((root/"evaluation.json").read_text())
            self.assertEqual(0 if evaluation["gate"]["qualified"] else 2, result.returncode)
            self.assertEqual((10, 4, 2), (len(evaluation["runs"]), len(evaluation["gate"]["perRunSuite"]),
                                        len(evaluation["gate"]["strata"])))

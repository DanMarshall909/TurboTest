"""Slow offline reproduction of the published TT-039 R4 and TT-038 rerun."""

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
sys.path.insert(0, str(HERE))

from replicated import fit, validate  # noqa: E402


def digest(payload):
    return hashlib.sha256(payload).hexdigest()


class R4ArchiveContract(unittest.TestCase):
    def test_fresh_fit_forecast_holdout_and_baseline_reproduce_offline(self):
        evidence = REPO / "docs/issues/evidence/2026-11-04/tt039-r4"
        manifest = json.loads((evidence / "evidence-manifest.json").read_text())
        archive_path = evidence / "raw-evidence.tar.gz"
        self.assertEqual(manifest["archiveSha256"], digest(archive_path.read_bytes()))

        with tempfile.TemporaryDirectory(prefix="tt039-r4-replay-") as temp:
            root = Path(temp)
            with tarfile.open(archive_path) as archive:
                self.assertEqual(set(manifest["files"]), set(archive.getnames()))
                for name in manifest["modelInputs"]:
                    payload = archive.extractfile(name).read()
                    self.assertEqual(manifest["files"][name], digest(payload), name)
                    target = root / name
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(payload)

                runtime_identity = json.loads(archive.extractfile("runtime-identity.json").read())
                self.assertEqual(
                    set(manifest["runtimeFiles"]),
                    {"runtime/" + name for name in runtime_identity},
                )
                for name, expected in runtime_identity.items():
                    payload = archive.extractfile("runtime/" + name).read()
                    self.assertEqual(expected, digest(payload), name)

            contract = json.loads((root / "contract.json").read_text())
            fitted = json.loads((root / "fit.json").read_text())
            frozen = json.loads((root / "forecast.json").read_text())
            saved = json.loads((root / "evaluation.json").read_text())
            self.assertEqual(
                {key: value for key, value in fitted.items() if key != "runtimeSeconds"},
                fit(root, contract),
            )
            replay = validate(root, contract, fitted, frozen)
            self.assertEqual(
                {key: value for key, value in saved.items() if key != "runtimeSeconds"},
                replay,
            )
            self.assertFalse(replay["gate"]["qualified"])
            self.assertEqual((140, 84, 8), (
                len(replay["runs"]),
                len(replay["gate"]["fullCohort"]["perRunSuite"]),
                len(replay["gate"]["fullCohort"]["strata"]),
            ))

        dependency = manifest["dependencies"]["tt038RuntimeArchive"]
        dependency_path = REPO / dependency["path"]
        self.assertEqual(dependency["sha256"], digest(dependency_path.read_bytes()))
        raw_manifest = manifest["dependencies"]["tt038RawManifest"]
        self.assertEqual(raw_manifest["sha256"], digest((REPO / raw_manifest["path"]).read_bytes()))
        with tempfile.TemporaryDirectory(prefix="tt038-rerun-replay-") as temp:
            task = Path(temp) / "tt038-baseline"
            with tarfile.open(archive_path) as archive:
                for name in manifest["baselineInputs"]:
                    payload = archive.extractfile(name).read()
                    self.assertEqual(manifest["files"][name], digest(payload), name)
                    relative = Path(name).relative_to("tt038-baseline")
                    target = task / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_bytes(payload)
            expected_summary = json.loads((task / "summary.json").read_text())
            (task / "summary.json").unlink()
            runtime_identity = json.loads((task / "v4/runtime-identity.json").read_text())
            self.assertEqual(
                set(manifest["baselineRuntimeFiles"]),
                {
                    f"tt038-baseline/v4/runtime-{mode}/{name}"
                    for mode, identities in runtime_identity.items()
                    for name in identities
                },
            )
            for mode, identities in runtime_identity.items():
                for name, expected in identities.items():
                    member = task / "v4" / f"runtime-{mode}" / name
                    self.assertEqual(expected, digest(member.read_bytes()), str(member))
            analyzer = task / "v4/sources/artifacts/tt038/analyze.py"
            self.assertTrue(analyzer.is_file(), str(analyzer))
            result = subprocess.run(
                [sys.executable, str(analyzer), str(task)],
                capture_output=True,
                text=True,
                timeout=120,
            )
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(expected_summary, json.loads((task / "summary.json").read_text()))


if __name__ == "__main__":
    unittest.main()

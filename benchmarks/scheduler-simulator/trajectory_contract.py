"""Slow command boundary for the whole-path reference."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from test_trajectory import cohort
from test_revised import trace_fixture
from test_trajectory_collector import collector


class WholePathCliContract(unittest.TestCase):
    def test_fit_and_forecast_precede_fresh_validation_and_preserve_existing_outputs(self):
        program=Path(__file__).with_name("trajectory.py")
        with tempfile.TemporaryDirectory(prefix="tt039-r3-cli-") as temp:
            root=Path(temp);contract,write=cohort(root)
            def run(action,output,*extra):
                return subprocess.run([sys.executable,str(program),action,"--trace-root",str(root),
                    "--output",str(root/output),*extra],capture_output=True,text=True,timeout=30)
            missing=run("validate","evaluation.json")
            self.assertNotEqual(0,missing.returncode)
            self.assertIn("requires --fit and --forecast",missing.stderr)
            fitted=run("fit","fit.json");self.assertEqual(0,fitted.returncode,fitted.stderr)
            original=(root/"fit.json").read_bytes()
            duplicate=run("fit","fit.json");self.assertEqual(1,duplicate.returncode)
            self.assertIn("TrajectoryInput:",duplicate.stderr)
            self.assertEqual(original,(root/"fit.json").read_bytes())
            frozen=run("forecast","forecast.json","--fit",str(root/"fit.json"))
            self.assertEqual(0,frozen.returncode,frozen.stderr)
            args=("--fit",str(root/"fit.json"),"--forecast",str(root/"forecast.json"))
            incomplete=run("validate","evaluation.json",*args)
            self.assertEqual(1,incomplete.returncode)
            self.assertFalse((root/"evaluation.json").exists())
            for row in contract["cases"]:
                if row["split"]!="validation":continue
                write(row["name"]+"/report.json",trace_fixture(row["mode"]))
                write(row["name"]+"/process.json",dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
            result=run("validate","evaluation.json",*args)
            self.assertEqual(0,result.returncode,result.stderr)
            evaluation=json.loads((root/"evaluation.json").read_text())
            self.assertTrue(evaluation["gate"]["qualified"])
            self.assertEqual((14,4,2),(len(evaluation["runs"]),len(evaluation["gate"]["perRunSuite"]),len(evaluation["gate"]["strata"])))


class TrainingArchiveContract(unittest.TestCase):
    def test_a_local_binding_must_match_the_real_committed_git_record(self):
        with tempfile.TemporaryDirectory(prefix="tt039-r3-git-") as temp:
            repo=Path(temp)/"repo";root=Path(temp)/"out";repo.mkdir();root.mkdir()
            def git(*args):
                return subprocess.check_output(["git",*args],cwd=repo,text=True,stderr=subprocess.STDOUT).strip()
            git("init","-q")
            expected=dict(sourceFiles={"sampler":"pinned"},files={"forecast":"pinned"})
            (repo/"binding.json").write_text(json.dumps(expected))
            git("add","binding.json")
            git("-c","user.name=Fixture","-c","user.email=fixture@example.invalid","commit","-q","-m","Freeze fixture")
            local=expected|dict(checkpoint=git("rev-parse","HEAD"))
            (root/"freeze.json").write_text(json.dumps(local))
            self.assertEqual(local,collector.committed_binding(root,"freeze.json","binding.json","checkpoint",repo=repo))
            (root/"freeze.json").write_text(json.dumps(local|dict(extra="tampered")))
            with self.assertRaisesRegex(ValueError,"FreezeRecord"):
                collector.committed_binding(root,"freeze.json","binding.json","checkpoint",repo=repo)

    def test_exact_published_training_is_extracted_with_original_roles_and_hashes(self):
        repo=Path(__file__).resolve().parents[2]
        evidence=repo/"docs/issues/evidence/2026-11-03/tt039-r2"
        manifest=json.loads((evidence/"evidence-manifest.json").read_text())
        with tempfile.TemporaryDirectory(prefix="tt039-r3-training-") as temp:
            root=Path(temp)
            provenance=collector.extract_training(root,evidence/"raw-evidence.tar.gz",manifest)
            self.assertEqual(84,len(provenance["files"]))
            historical=json.loads((root/"training/contract.json").read_text())
            self.assertEqual(24,sum(r["split"]=="calibration" for r in historical["cases"]))
            self.assertEqual(16,sum(r["split"]=="validation" for r in historical["cases"]))
            self.assertTrue((root/"training/r4-w500-background/report.json").is_file())
            altered=manifest|dict(archiveSha256="wrong")
            with self.assertRaisesRegex(ValueError,"TrainingArchive"):
                collector.extract_training(root/"bad",evidence/"raw-evidence.tar.gz",altered)

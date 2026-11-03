import copy
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest

PROGRAM=Path(__file__).resolve().parents[2]/"experiments/tt039/collect_r3.py"
spec=importlib.util.spec_from_file_location("r3_collector",PROGRAM)
collector=importlib.util.module_from_spec(spec);spec.loader.exec_module(collector)


class FreezeContract(unittest.TestCase):
    def test_missing_forecast_missing_file_and_changed_source_are_not_a_clean_freeze(self):
        with tempfile.TemporaryDirectory() as temp:
            repo=Path(temp)/"repo";root=Path(temp)/"out";repo.mkdir();root.mkdir()
            (repo/"sampler.py").write_text("frozen sampler")
            (root/"fit.json").write_text("frozen fit")
            (root/"forecast.json").write_text("frozen forecast")
            sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
            binding=dict(sourceFiles={"sampler.py":sha(repo/"sampler.py")},
                         files={name:sha(root/name) for name in ("fit.json","forecast.json")})
            collector.verify_files(repo,root,binding,{"fit.json","forecast.json"})
            omitted=copy.deepcopy(binding);del omitted["files"]["forecast.json"]
            with self.assertRaisesRegex(ValueError,"FreezeInputs.*forecast.json"):
                collector.verify_files(repo,root,omitted,{"fit.json","forecast.json"})
            (root/"forecast.json").unlink()
            with self.assertRaisesRegex(ValueError,"FreezeMissing.*forecast.json"):
                collector.verify_files(repo,root,binding,{"fit.json","forecast.json"})
            (root/"forecast.json").write_text("frozen forecast")
            (repo/"sampler.py").write_text("changed sampler")
            with self.assertRaisesRegex(ValueError,"FreezeSource.*sampler.py"):
                collector.verify_files(repo,root,binding,{"fit.json","forecast.json"})
            (repo/"sampler.py").write_text("frozen sampler")
            (root/"forecast.json").write_text("changed forecast")
            with self.assertRaisesRegex(ValueError,"FreezeData.*forecast.json"):
                collector.verify_files(repo,root,binding,{"fit.json","forecast.json"})

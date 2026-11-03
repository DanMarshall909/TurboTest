"""Slow real Git-record boundaries, using synthetic data and no native execution."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

PROGRAM=Path('experiments/tt039/collect_r4.py').resolve()
spec=importlib.util.spec_from_file_location('r4_freeze_collector',PROGRAM)
collector=importlib.util.module_from_spec(spec);spec.loader.exec_module(collector)


def frozen_fixture(base,*,qualification_exit=0):
    repo=base/'repo';root=base/'out';repo.mkdir();root.mkdir()
    write=lambda path,value:path.write_text(json.dumps(value,indent=2,sort_keys=True)+'\n')
    sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
    def git(*args):return subprocess.check_output(['git',*args],cwd=repo,text=True,stderr=subprocess.STDOUT).strip()
    def commit():
        git('add','.');git('-c','user.name=Fixture','-c','user.email=fixture@example.invalid','commit','-q','-m','Freeze fixture')
        return git('rev-parse','HEAD')
    sources=['experiments/tt039/'+n for n in ('collect.py','collect_r3.py','collect_r4.py','baseline_rerun.py','contract-r4.json')]
    sources+=['benchmarks/scheduler-simulator/'+n for n in
              ('model.py','reference.py','revised.py','trajectory.py','replicated.py','protocol-r4.md')]
    for name in sources:
        path=repo/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text('pinned '+name)
    source_ids={name:sha(repo/name) for name in sources}
    (root/'runtime').mkdir();(root/'runtime/PoolTrace.dll').write_bytes(b'pinned fixture bytes; never executed')
    for name in ('contract.json','image.json'):write(root/name,{'fixture':name})
    write(root/'source-identity.json',source_ids)
    write(root/'runtime-identity.json',{'PoolTrace.dll':sha(root/'runtime/PoolTrace.dll')})
    write(root/'qualification.json',dict(exitCode=qualification_exit,processGroupStopped=True,deadlineExceeded=False))
    (root/'calibration-input.json').write_text('fresh synthetic calibration input')
    base_files=['contract.json','source-identity.json','runtime-identity.json','image.json']
    for name in ('contract.json','contract-binding.json','runtime-identity.json','image.json','source-identity.json'):
        target=root/'tt038-baseline/v4'/name;target.parent.mkdir(parents=True,exist_ok=True);write(target,{'fixture':name})
        base_files.append('tt038-baseline/v4/'+name)
    protocol=dict(sourceFiles=source_ids,files={n:sha(root/n) for n in base_files})
    protocol_path=repo/'experiments/tt039/protocol-r4-binding.json';write(protocol_path,protocol)
    git('init','-q');protocol_commit=commit()
    write(root/'protocol-freeze.json',protocol|dict(checkpoint=protocol_commit))
    write(root/'measurement-window.json',dict(approvedBy='Dan',approvalReference='synthetic fixture; never executes native work',
        sourceCheckpoint=protocol_commit,scopes=['TT-038','TT-039'],validUntilUtc='2100-01-01T00:00:00+00:00'))
    common=base_files+['measurement-window.json','protocol-freeze.json','qualification.json']
    collection=dict(sourceFiles=source_ids,files={n:sha(root/n) for n in common+['calibration-input.json']})
    collection_path=repo/'experiments/tt039/collection-r4-binding.json';write(collection_path,collection)
    source_commit=commit()
    write(root/'collection-freeze.json',collection|dict(sourceCommit=source_commit))
    write(root/'fit.json',dict(trainingFiles={'calibration-input.json':sha(root/'calibration-input.json')}))
    write(root/'forecast.json',{'frozen':True})
    forecast=dict(sourceFiles=source_ids,files={n:sha(root/n) for n in common+['calibration-input.json','fit.json','forecast.json','collection-freeze.json']})
    forecast_path=repo/'benchmarks/scheduler-simulator/forecast-r4-binding.json';write(forecast_path,forecast)
    forecast_commit=commit();write(root/'forecast-freeze.json',forecast|dict(checkpoint=forecast_commit))
    return repo,root,forecast,forecast_path,write,commit


class CommittedFreezeContract(unittest.TestCase):
    def test_calibration_requires_the_source_freeze_but_never_requires_future_forecasts(self):
        with tempfile.TemporaryDirectory() as temp:
            repo,root,*_=frozen_fixture(Path(temp))
            for name in ('fit.json','forecast.json','forecast-freeze.json'):(root/name).unlink()
            collector.check_freeze(root,repo=repo,phase='calibration')
            with self.assertRaisesRegex(ValueError,'FreezeMissing.*forecast-freeze.json'):
                collector.check_freeze(root,repo=repo,phase='validation')

    def test_valid_records_reject_changed_forecast_missing_pin_and_runtime_drift(self):
        with tempfile.TemporaryDirectory() as temp:
            repo,root,frozen,path,write,commit=frozen_fixture(Path(temp))
            collector.check_freeze(root,repo=repo)
            original=(root/'forecast.json').read_bytes();(root/'forecast.json').write_text('changed')
            with self.assertRaisesRegex(ValueError,'FreezeData.*forecast.json'):collector.check_freeze(root,repo=repo)
            (root/'forecast.json').write_bytes(original)
            (root/'runtime/PoolTrace.dll').write_bytes(b'changed runtime')
            with self.assertRaisesRegex(ValueError,'NativeIdentity'):collector.check_freeze(root,repo=repo)
            (root/'runtime/PoolTrace.dll').write_bytes(b'pinned fixture bytes; never executed')
            del frozen['files']['forecast.json'];write(path,frozen)
            write(root/'forecast-freeze.json',frozen|dict(checkpoint=commit()))
            with self.assertRaisesRegex(ValueError,'FreezeInputs.*forecast.json'):collector.check_freeze(root,repo=repo)

    def test_a_failed_native_qualification_receipt_cannot_be_a_collection_freeze(self):
        with tempfile.TemporaryDirectory() as temp:
            repo,root,*_=frozen_fixture(Path(temp),qualification_exit=1)
            with self.assertRaisesRegex(ValueError,'FreezeQualification'):collector.check_freeze(root,repo=repo)

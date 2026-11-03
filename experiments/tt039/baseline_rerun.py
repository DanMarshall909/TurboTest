"""Fresh TT-038 v4 comparison in a new root, behind the shared owner window."""
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import time

REPO=Path(__file__).resolve().parents[2]
SESSION=REPO/'artifacts/tt039-r4'
TASK=SESSION/'tt038-baseline'
ROOT=TASK/'v4'


def load(path,name):
    spec=importlib.util.spec_from_file_location(name,path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module


def session_driver():
    return load(Path(__file__).with_name('collect_r4.py'),'tt038_rerun_session')


def native_driver(root=ROOT):
    # The verified historical driver is imported into a separate module instance.
    # Its original files/output roots are never changed.
    module=load(REPO/'artifacts/tt038/run.py','tt038_rerun_native')
    module.ROOT=Path(root)
    return module


def prepare(session=SESSION):
    session=Path(session);root=session/'tt038-baseline/v4'
    if root.exists():raise FileExistsError('BaselineExisting: retain previous comparison')
    previous=REPO/'artifacts/tt038/v4'
    driver=native_driver(root)
    expected=json.loads((previous/'runtime-identity.json').read_text())
    source_ids=json.loads((previous/'source-identity.json').read_text())
    published=json.loads((REPO/'docs/issues/evidence/2026-11-02/tt038/raw-manifest.json').read_text())
    for name in ('contract.json','contract-binding.json','runtime-identity.json','source-identity.json','image.json'):
        if driver.digest(previous/name)!=published['v4/'+name]['sha256']:
            raise ValueError('BaselineInput: retained published metadata differs: '+name)
    if driver.digest(REPO/'artifacts/tt038/analyze.py')!=published['analyze.py']['sha256']:
        raise ValueError('BaselineSource: original analysis differs')
    contract=(previous/'contract.json').read_bytes()
    if driver.digest(previous/'contract.json')!=json.loads((previous/'contract-binding.json').read_text())['sha256']:
        raise ValueError('BaselineContract: retained v4 contract differs')
    for name,sha in source_ids.items():
        if driver.digest(REPO/name)!=sha:raise ValueError('BaselineSource: changed v4 source '+name)
    for mode in ('awaited','background'):
        if driver.identity(previous/('runtime-'+mode))!=expected[mode]:
            raise ValueError('BaselineRuntime: retained v4 '+mode+' executable differs')
    if expected['awaited'].keys()!=expected['background'].keys() or [n for n in expected['awaited'] if expected['awaited'][n]!=expected['background'][n]]!=['TurboTest.EFCore.dll']:
        raise ValueError('BaselineRuntime: only the production library may differ')
    root.mkdir(parents=True)
    for mode in ('awaited','background'):(root/('runtime-'+mode)).symlink_to(previous/('runtime-'+mode),target_is_directory=True)
    (root/'contract.json').write_bytes(contract)
    for name in ('contract-binding.json','runtime-identity.json','image.json'):shutil.copyfile(previous/name,root/name)
    source_ids['experiments/tt039/baseline_rerun.py']=driver.digest(Path(__file__))
    source_ids['artifacts/tt038/analyze.py']=driver.digest(REPO/'artifacts/tt038/analyze.py')
    return source_ids


def main():
    action=sys.argv[1]
    session=session_driver()
    if action=='analyze':
        session.check_protocol_freeze(SESSION)
        if (TASK/'summary.json').exists():raise FileExistsError('BaselineExisting: preserve summary')
        session.shared.native_driver(SESSION).verify.run_phase('analysis-process',
            [sys.executable,str(REPO/'artifacts/tt038/analyze.py'),str(TASK)],TASK,120)
        return
    session.require_measurement_window(SESSION,scope='TT-038')
    session.check_protocol_freeze(SESSION)
    driver=native_driver()
    authorize=lambda:session.require_measurement_window(SESSION,scope='TT-038',reserve_seconds=480)
    if action=='qualify':
        names=['smoke-'+mode for mode in ('awaited','background')]+['fault-'+mode for mode in ('awaited','background')]
        if any((ROOT/name).exists() for name in names):raise FileExistsError('BaselineExisting: qualification already attempted')
        for mode in ('awaited','background'):
            authorize();driver.run_case('smoke-'+mode,mode,0,16,8)
            authorize();driver.run_case('fault-'+mode,mode,0,8,4,'distribution')
    elif action=='measure':
        receipt=json.loads((ROOT/'qualification.json').read_text())
        if receipt['exitCode']!=0 or not receipt['processGroupStopped'] or receipt['deadlineExceeded']:
            raise ValueError('BaselineQualification: native controls must complete first')
        contract=json.loads((ROOT/'contract.json').read_text())
        session.collect_cases(ROOT,contract['cases'],driver,clock=time.monotonic,authorize=authorize,phase='baseline')
    else:raise ValueError('BaselineAction: qualify, measure or analyze required')


if __name__=='__main__':main()

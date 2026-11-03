"""Fixed-cohort TT-039 collector; only a fully completed frozen cohort can qualify."""

import json
import hashlib
import importlib.util
import math
from datetime import datetime,timezone,timedelta
from pathlib import Path
import shutil
import sys
import tarfile
import time

REPO=Path(__file__).resolve().parents[2]
ROOT=REPO/'artifacts/tt039-r4'
spec=importlib.util.spec_from_file_location('tt039_r4_shared',Path(__file__).with_name('collect_r3.py'))
shared=importlib.util.module_from_spec(spec);spec.loader.exec_module(shared)


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def require_measurement_window(root=ROOT,*,scope='TT-039',reserve_seconds=0):
    root=Path(root);path=root/'measurement-window.json'
    if (root/'STOP_PERFORMANCE').exists():raise PermissionError('PerformanceConfirmation: approval withdrawn')
    if not path.is_file():raise PermissionError('PerformanceConfirmation: Dan must confirm the measurement window first')
    window=json.loads(path.read_text())
    if window.get('approvedBy')!='Dan' or not window.get('approvalReference') or scope not in window.get('scopes',[]):
        raise PermissionError('PerformanceConfirmation: missing explicit approval for '+scope)
    expires=datetime.fromisoformat(window['validUntilUtc'])
    if expires.tzinfo is None or expires<=datetime.now(timezone.utc):
        raise PermissionError('PerformanceConfirmation: measurement window expired')
    if expires<datetime.now(timezone.utc)+timedelta(seconds=reserve_seconds):
        raise PermissionError('PerformanceConfirmation: window expires before case settlement')
    frozen=root/'protocol-freeze.json'
    if not frozen.is_file() or window.get('sourceCheckpoint')!=json.loads(frozen.read_text())['checkpoint']:
        raise PermissionError('PerformanceConfirmation: approved source differs')
    return window


def collect_cases(root,cases,driver,*,clock,authorize,budget_seconds=7200,phase='collection'):
    root=Path(root)
    existing=[row['name'] for row in cases if (root/row['name']).exists()]
    if existing:raise FileExistsError('CollectionExisting: '+', '.join(existing))
    started=clock();completed=[];failure=None
    try:
        for row in cases:
            authorize()
            if (root/'STOP_PERFORMANCE').exists():raise PermissionError('PerformanceConfirmation: approval withdrawn')
            if clock()-started+480>budget_seconds:
                raise TimeoutError('CollectionBudget: insufficient time for another case and settlement')
            driver.run_case(row['name'],row['mode'],row['waitMilliseconds'],256,32)
            completed.append(row['name'])
    except BaseException as error:
        failure=type(error).__name__+': '+str(error)
        raise
    finally:
        state=dict(phase=phase,expected=len(cases),completed=completed,complete=len(completed)==len(cases),
                   budgetSeconds=budget_seconds,elapsedSeconds=clock()-started,failure=failure)
        (root/(phase+'-progress.json')).write_text(json.dumps(state,indent=2,sort_keys=True)+'\n')


def check_control_result(command):
    if command[2]=='held-control':
        mode=command[3];result=json.loads((Path(command[4])/'control.json').read_text())
        flags=('Passed','CleanAlternate','ExhaustedAcquisitionWaited','ShutdownJoinedReset')
        if (result.get('Mode')!=mode or result.get('HeldReceipt') is not (mode=='awaited')
                or not all(result.get(key) is True for key in flags)):
            raise ValueError('ControlResult: held-reset behavior differs')
    elif command[2]=='probe-cost':
        rows=json.loads(Path(command[3]).read_text())
        expected={(enabled,round_) for enabled in (False,True) for round_ in range(3)}
        if (len(rows)!=6 or {(r['Enabled'],r['Round']) for r in rows}!=expected
                or not all(type(r['Enabled']) is bool and type(r['Round']) is int and r['Count']==50000
                    and math.isfinite(r['MicrosecondsPerEvent']) and r['MicrosecondsPerEvent']>=0 for r in rows)):
            raise ValueError('ControlResult: probe-cost rounds/counts/costs differ')
    else:raise ValueError('ControlResult: unknown native control')


def run_control(driver,name,command,deadline):
    # Held-reset and probe controls bypass run_case, but retain its source and
    # resource-settlement boundary even when the supervised process fails.
    check_protocol_freeze()
    before=driver.inventory()
    try:
        driver.verify.run_phase(name,command,ROOT,deadline,driver.ENV)
        check_control_result(command)
    finally:
        after=driver.inventory()
        settlement=dict(before=before,after=after,unchanged=before==after)
        (ROOT/(name+'-settlement.json')).write_text(json.dumps(settlement,indent=2,sort_keys=True)+'\n')
        check_protocol_freeze()
        if before!=after:raise ValueError('ControlSettlement: Docker inventory changed during '+name)


def check_protocol_freeze(root=ROOT,*,repo=REPO):
    root=Path(root);repo=Path(repo)
    read=lambda name:json.loads((root/name).read_text())
    common={'contract.json','source-identity.json','runtime-identity.json','image.json'}
    common|={'tt038-baseline/v4/'+name for name in ('contract.json','contract-binding.json','runtime-identity.json','image.json','source-identity.json')}
    source=shared.committed_binding(root,'protocol-freeze.json','experiments/tt039/protocol-r4-binding.json','checkpoint',repo=repo)
    shared.verify_files(repo,root,source,common)
    required={'experiments/tt039/'+name for name in ('collect.py','collect_r3.py','collect_r4.py','baseline_rerun.py','contract-r4.json')}
    required|={'benchmarks/scheduler-simulator/'+name for name in
               ('model.py','reference.py','revised.py','trajectory.py','replicated.py','protocol-r4.md')}
    if source['sourceFiles']!=read('source-identity.json') or not required<=source['sourceFiles'].keys():
        raise ValueError('FreezeSource: complete model/collector source binding required')
    expected=read('runtime-identity.json')
    if 'PoolTrace.dll' not in expected or shared.native_driver(root).identity(root/'runtime')!=expected:
        raise ValueError('NativeIdentity: pinned shared runtime changed')
    return source


def check_freeze(root=ROOT,*,repo=REPO,phase='validation'):
    root=Path(root);repo=Path(repo);read=lambda name:json.loads((root/name).read_text())
    protocol=check_protocol_freeze(root,repo=repo)
    common={'contract.json','source-identity.json','runtime-identity.json','image.json',
            'measurement-window.json','protocol-freeze.json','qualification.json'}
    source=shared.committed_binding(root,'collection-freeze.json','experiments/tt039/collection-r4-binding.json','sourceCommit',repo=repo)
    if source['sourceFiles']!=protocol['sourceFiles']:raise ValueError('FreezeSource: collection/protocol bindings differ')
    shared.verify_files(repo,root,source,common)
    qualification=read('qualification.json')
    if qualification['exitCode']!=0 or not qualification['processGroupStopped'] or qualification['deadlineExceeded']:
        raise ValueError('FreezeQualification: native routing did not complete and settle')
    if phase=='calibration':return
    if phase!='validation':raise ValueError('FreezePhase: calibration or validation required')
    forecast=shared.committed_binding(root,'forecast-freeze.json','benchmarks/scheduler-simulator/forecast-r4-binding.json','checkpoint',repo=repo)
    if forecast['sourceFiles']!=source['sourceFiles']:raise ValueError('FreezeSource: forecast/source bindings differ')
    mandatory=common|{'fit.json','forecast.json','collection-freeze.json'}
    shared.verify_files(repo,root,forecast,mandatory)
    training=read('fit.json')['trainingFiles']
    if not training:raise ValueError('FreezeInputs: empty training binding')
    shared.verify_files(repo,root,forecast,mandatory|set(training))


def check_contract(contract):
    sys.path.insert(0,str(REPO/'benchmarks/scheduler-simulator'))
    from replicated import calibration_cases,future_cases
    fixed=dict(slots=8,workers=4,resetConcurrency=4,cycles=256,warmups=32,
               collectionBudgetSeconds=7200,caseDeadlineSeconds=300,caseReserveSeconds=480)
    if any(contract.get(k)!=v for k,v in fixed.items()):raise ValueError('CollectionContract: fixed workload and resource envelope required')
    required={(mode,wait) for mode in ('awaited','background') for wait in (0,25,100,500)}
    if contract['cases']!=calibration_cases(required)+future_cases(required):
        raise ValueError('CollectionContract: 56 fresh calibration and 84 fresh validation cases required')
    if contract['protocolSha256']!=digest(REPO/'benchmarks/scheduler-simulator/protocol-r4.md'):
        raise ValueError('CollectionContract: declared protocol differs')


def prepare(root=ROOT):
    root=Path(root);root.mkdir(parents=True,exist_ok=True)
    if any((root/name).exists() for name in ('runtime','contract.json')):
        raise FileExistsError('PrepareExisting: retain the previous attempt')
    evidence=REPO/'docs/issues/evidence/2026-11-03/tt039-r3'
    manifest=json.loads((evidence/'evidence-manifest.json').read_text())
    archive=evidence/'raw-evidence.tar.gz'
    if digest(archive)!=manifest['archiveSha256']:raise ValueError('TrainingArchive: changed source archive')
    with tarfile.open(archive) as source:
        runtime_identity=json.loads(source.extractfile('runtime-identity.json').read())
        native_sources=json.loads(source.extractfile('source-identity.json').read())
        image=source.extractfile('image.json').read()
    contract=json.loads((Path(__file__).with_name('contract-r4.json')).read_text())
    check_contract(contract)
    runtime=REPO/'artifacts/tt039-r2/runtime';driver=shared.native_driver(root)
    if driver.identity(runtime)!=runtime_identity:raise ValueError('NativeIdentity: exact R2 executable unavailable or changed')
    for name,expected in native_sources.items():
        if digest(REPO/name)!=expected:raise ValueError('NativeSource: changed archived source '+name)
    (root/'runtime-identity.json').write_text(json.dumps(runtime_identity,indent=2,sort_keys=True)+'\n')
    (root/'image.json').write_bytes(image)
    shutil.copyfile(Path(__file__).with_name('contract-r4.json'),root/'contract.json')
    (root/'runtime').symlink_to(runtime,target_is_directory=True)
    extra=['benchmarks/scheduler-simulator/replicated.py','benchmarks/scheduler-simulator/protocol-r4.md',
           'experiments/tt039/collect_r4.py','experiments/tt039/contract-r4.json']
    identities=native_sources|{name:digest(REPO/name) for name in extra}
    baseline_spec=importlib.util.spec_from_file_location('tt039_baseline_prepare',Path(__file__).with_name('baseline_rerun.py'))
    baseline=importlib.util.module_from_spec(baseline_spec);baseline_spec.loader.exec_module(baseline)
    baseline_sources=baseline.prepare(root)
    if any(name in identities and identities[name]!=sha for name,sha in baseline_sources.items()):
        raise ValueError('BaselineSource: shared source identities disagree')
    identities|=baseline_sources
    (root/'source-identity.json').write_text(json.dumps(identities,indent=2,sort_keys=True)+'\n')
    (root/'tt038-baseline/v4/source-identity.json').write_text(json.dumps(identities,indent=2,sort_keys=True)+'\n')
    for name in identities:
        target=root/'source-snapshots'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(REPO/name,target)
        target=root/'tt038-baseline/v4/sources'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(REPO/name,target)
    print('Prepared fresh 56-calibration/84-validation manifest; native executable is a pinned read-only input.',flush=True)


def main():
    action=sys.argv[1]
    if action=='prepare':prepare();return
    require_measurement_window()
    check_protocol_freeze()
    driver=shared.native_driver(ROOT)
    if action=='qualify':
        started=time.monotonic()
        def authorize_control(reserve):
            require_measurement_window(reserve_seconds=reserve)
            if time.monotonic()-started+reserve>3600:
                raise TimeoutError('QualificationBudget: insufficient time for another control and settlement')
        paths=['qualified-'+kind+'-'+mode for kind in ('smoke','fault','held') for mode in ('awaited','background')]
        paths+=['probe-cost.json']+[f'overhead-r{pair}-'+state for pair in range(3) for state in ('on','off')]
        if any((ROOT/path).exists() for path in paths):raise FileExistsError('QualificationExisting: retain previous controls')
        for mode in ('awaited','background'):
            authorize_control(480)
            driver.run_case('qualified-smoke-'+mode,mode,0,16,8)
            authorize_control(480)
            driver.run_case('qualified-fault-'+mode,mode,0,8,4,'distribution')
            authorize_control(480)
            run_control(driver,'held-'+mode+'-process',['dotnet',str(ROOT/'runtime/PoolTrace.dll'),
                'held-control',mode,str(ROOT/('qualified-held-'+mode))],300)
        authorize_control(60)
        run_control(driver,'probe-cost-process',['dotnet',str(ROOT/'runtime/PoolTrace.dll'),
            'probe-cost',str(ROOT/'probe-cost.json')],30)
        for pair in range(3):
            for enabled in ([True,False] if pair%2==0 else [False,True]):
                authorize_control(480)
                driver.run_case(f'overhead-r{pair}-'+('on' if enabled else 'off'),'background',0,64,32,
                                None if enabled else 'trace-off')
    elif action in ('calibrate','validate'):
        phase='calibration' if action=='calibrate' else 'validation'
        check_freeze(phase=phase)
        contract=json.loads((ROOT/'contract.json').read_text())
        check_contract(contract)
        cases=[r for r in contract['cases'] if r['split']==phase]
        collect_cases(ROOT,cases,driver,clock=time.monotonic,
                      authorize=lambda:require_measurement_window(reserve_seconds=480),phase=phase)
        check_freeze(phase=phase)
    else:raise ValueError('CollectionAction: prepare, qualify, calibrate or validate required')


if __name__=='__main__':
    main()

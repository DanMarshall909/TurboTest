"""One-off source-bound TT-039 revised-pipeline trace collection; not a maintained runner."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import threading
import time

REPO = Path(__file__).resolve().parents[2]
ROOT = REPO / 'artifacts/tt039-r2'
TASK = Path(__file__).resolve().parent
sys.path.insert(0, str(REPO))
from scripts import verify
sys.path.insert(0, str(REPO/'benchmarks/scheduler-simulator'))
from revised import read_trace

ENV = dict(os.environ, DOCKER_HOST='unix:///var/run/docker.sock')
DOCKER = ['docker', '--host', ENV['DOCKER_HOST']]

def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def identity(path):
    return {str(p.relative_to(path)): digest(p) for p in sorted(path.rglob('*')) if p.is_file()}

def docker(*args):
    return subprocess.check_output(DOCKER + list(args), text=True, timeout=15)

def inventory():
    return {'containers': sorted(docker('ps', '--all', '--no-trunc', '--format', '{{.ID}}').splitlines()),
            'volumes': sorted(docker('volume', 'ls', '--format', '{{.Name}}').splitlines())}

def host_observation():
    return {'utcEpochSeconds': time.time(), 'monotonicSeconds': time.monotonic(), 'loadAverage': os.getloadavg(), 'logicalCpuCount': os.cpu_count(),
            'pressure': {key: Path('/proc/pressure', key).read_text() for key in ['cpu','io','memory']},
            'cpu': Path('/proc/stat').read_text().splitlines()[0]}


def bind():
    assert not (ROOT/'runtime-identity.json').exists(), 'Preserve existing binding'
    verify.write_json(ROOT/'runtime-identity.json', identity(ROOT/'runtime'))
    sources = list(TASK.glob('*.py')) + list(TASK.glob('*.cs')) + list(TASK.glob('*.csproj'))
    sources += [TASK/'contract.json', TASK/'packages.lock.json']
    sources += [REPO/'Directory.Build.props', REPO/'global.json']
    sources += [REPO/'benchmarks/scheduler-simulator'/name for name in ('model.py','reference.py','revised.py','protocol-r2.md')]
    sources += [p for p in (REPO/'src/TurboTest.EFCore').rglob('*.cs') if not {'bin','obj'} & set(p.parts)]
    sources += [REPO/'benchmarks/TurboTest.Benchmarks/BenchmarkWorkload.cs', REPO/'benchmarks/TurboTest.Benchmarks/WorkloadMix.cs', REPO/'scripts/verify.py']
    verify.write_json(ROOT/'source-identity.json', {str(p.relative_to(REPO)): digest(p) for p in sources})
    verify.write_json(ROOT/'image.json', json.loads(docker('image','inspect','postgres:16-alpine'))[0])


def run_case(name, mode, delay, cycles, warmups, fault=None):
    out = ROOT/name; out.mkdir()
    expected = json.loads((ROOT/'runtime-identity.json').read_text())
    assert identity(ROOT/'runtime') == expected
    for path, value in json.loads((ROOT/'source-identity.json').read_text()).items():
        assert digest(REPO/path) == value, path
    before = inventory()
    verify.write_json(out/'before.json', before)
    image_id = json.loads(docker('image', 'inspect', 'postgres:16-alpine'))[0]['Id']
    assert image_id == json.loads((ROOT/'image.json').read_text())['Id']
    verify.write_json(out/'host.json', host_observation())
    stopped = threading.Event(); observations = []; monitor_errors = []; pressure = []
    def monitor():
        known = set()
        while not stopped.wait(.5):
            try:
                pressure.append(host_observation())
                progress = out/'progress.json'
                if not progress.exists(): continue
                prefix = json.loads(progress.read_text())['Prefix']
                ids = docker('ps', '--all', '--filter', 'name='+prefix, '--format', '{{.ID}}').splitlines()
                for cid in ids:
                    if cid in known: continue
                    data = json.loads(docker('inspect', cid))[0]
                    host = data['HostConfig']
                    observations.append({'id': data['Id'], 'name': data['Name'], 'image': data['Image'],
                        'mounts': [{k:m.get(k) for k in ['Type','Name','Destination']} for m in data['Mounts']],
                        'resources': {k:host.get(k) for k in ['Memory','MemorySwap','NanoCpus','CpuQuota','CpuPeriod','CpuShares','NetworkMode']}})
                    known.add(cid)
            except Exception as error:
                monitor_errors.append(type(error).__name__+': '+str(error))
    thread = threading.Thread(target=monitor); thread.start()
    failed = None
    try:
        command = ['dotnet', str(ROOT/'runtime'/'PoolTrace.dll'), str(out), mode, str(delay), str(cycles), str(warmups)]
        if fault: command.append(fault)
        verify.run_phase('process', command, out, 300, ENV)
    except verify.VerificationError as error:
        failed = str(error)
    finally:
        stopped.set(); thread.join(timeout=20)
        assert not thread.is_alive(), 'MonitorWorkers'
        after = inventory()
        verify.write_json(out/'after.json', after)
        verify.write_json(out/'resources.json', {'hosts': observations, 'monitorErrors': monitor_errors, 'hostPressure': pressure})
        verify.write_json(out/'settlement.json', {'unchanged': before == after})
        assert before == after, 'ResourceSettlement'
        assert identity(ROOT/'runtime') == expected
        for path, value in json.loads((ROOT/'source-identity.json').read_text()).items():
            assert digest(REPO/path) == value, 'Source drift after case: '+path
    result = json.loads((out/'report.json').read_text())
    if fault == 'distribution':
        assert failed and not result['Succeeded'] and result['CleanupCompleted'], result
        assert any('WorkloadDistribution' in error for error in result['Failures']), result['Failures']
    else:
        assert failed is None, failed
        assert result['Succeeded'] and result['CleanupCompleted'] and result['IsolationChecked']
        assert len(result['Samples']) == cycles
        assert len(result['Resets']) == 8 + warmups + cycles + 8
        assert len(result['KnownDatabases']) == 8
        assert all(r['Succeeded'] and r['Strategy'] == 'Rows' for r in result['Resets'])
        assert set(result['PostgreSqlSettings']) == {'fsync','synchronous_commit','full_page_writes','wal_level','jit',
            'autovacuum','shared_buffers','max_connections','track_io_timing','track_wal_io_timing'}
        if fault != 'trace-off':
            read_trace(result, dict(slots=8, workers=4, resetConcurrency=4, cycles=cycles))
    assert len(observations) == 1, observations
    assert observations[0]['image'] == image_id
    limits = observations[0]['resources']
    assert limits['Memory'] == 1024*1024*1024
    print(name, 'EXPECTED FAULT' if fault == 'distribution' else 'PASS', 'suiteMs', round(result['SuiteMilliseconds'], 2), flush=True)
    return result


def check_freeze(phase):
    assert (ROOT/'collection-freeze.json').is_file(), 'CollectionFreeze: missing source checkpoint'
    frozen = json.loads((ROOT/'collection-freeze.json').read_text())
    committed = json.loads(subprocess.check_output(['git','show',frozen['sourceCommit']+':experiments/tt039/collection-binding.json'],cwd=REPO,text=True))
    assert committed == {k:v for k,v in frozen.items() if k != 'sourceCommit'}, 'CollectionFreeze: committed binding'
    assert frozen['sourceFiles'] == json.loads((ROOT/'source-identity.json').read_text()), 'CollectionFreeze: sources'
    assert digest(ROOT/'contract.json') == frozen['contractSha256'], 'CollectionFreeze: contract'
    assert identity(ROOT/'runtime') == json.loads((ROOT/'runtime-identity.json').read_text()), 'CollectionFreeze: runtime'
    assert not subprocess.run(['git','merge-base','--is-ancestor',frozen['sourceCommit'],'HEAD'],cwd=REPO).returncode, 'CollectionFreeze: source checkpoint'
    qualification = json.loads((ROOT/'qualification-final.json').read_text())
    assert qualification['exitCode'] == 0 and qualification['processGroupStopped'], 'CollectionFreeze: native qualification'
    for name, expected in frozen['qualificationFiles'].items():
        assert digest(ROOT/name) == expected, 'CollectionFreeze: qualification '+name
    if phase == 'validate':
        assert (ROOT/'forecast-freeze.json').is_file(), 'ForecastFreeze: missing prediction checkpoint'
        prediction = json.loads((ROOT/'forecast-freeze.json').read_text())
        committed = json.loads(subprocess.check_output(['git','show',prediction['checkpoint']+':benchmarks/scheduler-simulator/forecast-r2-binding.json'],cwd=REPO,text=True))
        assert committed == {k:v for k,v in prediction.items() if k != 'checkpoint'}, 'ForecastFreeze: committed binding'
        for name, expected in prediction['files'].items():
            assert digest(ROOT/name) == expected, 'ForecastFreeze: '+name
        for name, expected in prediction['sourceFiles'].items():
            assert digest(REPO/name) == expected, 'ForecastFreeze: source '+name
        assert not subprocess.run(['git','merge-base','--is-ancestor',prediction['checkpoint'],'HEAD'],cwd=REPO).returncode, 'ForecastFreeze: checkpoint'

if __name__ == '__main__':
    os.chdir(REPO)
    if sys.argv[1] == 'bind': bind()
    elif sys.argv[1] in ('qualify','qualify-final'):
        label = '' if sys.argv[1] == 'qualify' else 'qualified-'
        for mode in ['awaited', 'background']:
            run_case(label+'smoke-'+mode, mode, 0, 16, 8)
            run_case(label+'fault-'+mode, mode, 0, 8, 4, 'distribution')
    elif sys.argv[1] == 'perturbation':
        for pair in range(3):
            for enabled in ([True,False] if pair%2 == 0 else [False,True]):
                run_case(f'overhead-r{pair}-'+('on' if enabled else 'off'), 'background', 0, 64, 32,
                         None if enabled else 'trace-off')
    elif sys.argv[1] in ('calibrate','validate'):
        check_freeze(sys.argv[1])
        contract = json.loads((ROOT/'contract.json').read_text())
        for case in contract['cases']:
            if case['split'] != ('calibration' if sys.argv[1] == 'calibrate' else 'validation'): continue
            run_case(case['name'], case['mode'], case['waitMilliseconds'], contract['cycles'], contract['warmups'])

#!/usr/bin/env python3
"""Reproducible Linux correctness verification for TurboTest."""

import xml.etree.ElementTree as ET
import json
import os
from pathlib import Path
import signal
import subprocess
import time


class VerificationError(RuntimeError):
    pass


def discovered_tests(output):
    return [line.strip() for line in output.splitlines()
            if line.startswith("    TurboTest.Tests.")]


def reconcile_lanes(all_rows, fast, slow):
    if (not all_rows or any(len(rows) != len(set(rows)) for rows in (all_rows, fast, slow))
            or set(fast) & set(slow) or set(fast) | set(slow) != set(all_rows)):
        raise VerificationError("LanePartition: discovery must be nonempty, unique and partitioned exactly")
    return {"all": sorted(all_rows), "fast": sorted(fast), "slow": sorted(slow)}


def read_results(path, expected):
    try:
        tree = ET.parse(path)
        ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
        root = tree.getroot()
        if (root.tag != ns + "TestRun" or len(root.findall(ns + "Results")) != 1
                or len(list(root.iter(ns + "ResultSummary"))) != 1
                or len(root.findall(ns + "ResultSummary")) != 1):
            raise ValueError("invalid TRX structure")
        results = root.find(ns + "Results")
        if len(list(root.iter(ns + "UnitTestResult"))) != len(results.findall(ns + "UnitTestResult")):
            raise ValueError("misplaced test result")
        rows = []
        for node in results.findall(ns + "UnitTestResult"):
            hours, minutes, seconds = node.attrib["duration"].split(":")
            if int(hours) < 0 or not 0 <= int(minutes) < 60 or not 0 <= float(seconds) < 60:
                raise ValueError("invalid duration")
            rows.append({"id": node.attrib["testId"], "name": node.attrib["testName"],
                         "outcome": node.attrib["outcome"],
                         "seconds": int(hours) * 3600 + int(minutes) * 60 + float(seconds)})
        ids = [row["id"] for row in rows]
        summary = root.find(ns + "ResultSummary")
        if len(summary.findall(ns + "Counters")) != 1 or len(list(root.iter(ns + "Counters"))) != 1:
            raise ValueError("invalid run counter placement")
        counters = summary.find(ns + "Counters")
        if summary.get("outcome") not in ("Completed", "Passed"):
            raise ValueError("missing or unsuccessful run summary")
        for field in ("total", "executed", "passed"):
            if int(counters.attrib[field]) != len(expected):
                raise ValueError("run counters disagree with expected execution")
        if any(int(value) != 0 for key, value in counters.attrib.items() if key not in ("total", "executed", "passed")):
            raise ValueError("run counters include unsuccessful results")
        if any(node.get("outcome") in ("Error", "Failed", "Aborted") for node in tree.iter() if node.tag.endswith("RunInfo")):
            raise ValueError("run-level error")
        if (not all(ids) or len(ids) != len(set(ids))
                or sorted(row["name"] for row in rows) != sorted(expected)
                or not rows or any(row["outcome"] != "Passed" for row in rows)):
            raise VerificationError("TestResults: missing, duplicate, unexpected, skipped or failed rows")
        return rows
    except (OSError, ET.ParseError, KeyError, ValueError) as error:
        raise VerificationError(f"TestResults: invalid TRX: {error}") from error


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")


def group_members(group):
    members = []
    for path in Path("/proc").glob("[0-9]*/stat"):
        try:
            # comm can contain spaces and parentheses; the suffix begins with state.
            values = path.read_text().rsplit(")", 1)[1].split()
            if int(values[2]) == group and values[0] != "Z":
                members.append(int(path.parent.name))
        except (OSError, ValueError, IndexError):
            continue  # A process can exit while /proc is enumerated.
    return members


def stop_group(process):
    for sig, grace in ((signal.SIGTERM, 1), (signal.SIGKILL, 2)):
        try:
            os.killpg(process.pid, sig)
        except ProcessLookupError:
            break
        until = time.monotonic() + grace
        while group_members(process.pid) and time.monotonic() < until:
            time.sleep(.02)
    process.wait(timeout=3)
    return not group_members(process.pid)


def run_phase(name, command, out, deadline, env=None):
    start = time.monotonic()
    receipt = {"command": command, "cwd": str(Path.cwd()), "deadlineSeconds": deadline,
               "deadlineExceeded": False, "processGroupStopped": None}
    process = None
    failure = None
    try:
        with (out / (name + ".log")).open("w") as log:
            process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT,
                                       env=env, start_new_session=True)
            receipt["processGroupId"] = process.pid
            try:
                process.wait(timeout=deadline)
            except subprocess.TimeoutExpired:
                receipt["deadlineExceeded"] = True
                receipt["processGroupStopped"] = stop_group(process)
                failure = f"PhaseDeadline: {name} exceeded {deadline}s"
            if group_members(process.pid):
                receipt["processGroupStopped"] = stop_group(process)
                if failure is None:
                    failure = f"PhaseWorkers: {name} left live owned descendants"
            else:
                receipt["processGroupStopped"] = True
            if process.returncode and failure is None:
                failure = f"PhaseFailed: {name} exited {process.returncode}"
    except BaseException as error:
        receipt["interruption"] = f"{type(error).__name__}: {error}"
        if process is not None:
            receipt["processGroupStopped"] = stop_group(process)
        raise
    finally:
        receipt["exitCode"] = process.returncode if process else None
        receipt["elapsedSeconds"] = time.monotonic() - start
        write_json(out / (name + ".json"), receipt)
    if failure:
        raise VerificationError(failure)
    return (out / (name + ".log")).read_text()


def discovery_ids(output, expected):
    marker = "TestRequestSender.OnDiscoveryMessageReceived: Received message: "
    tests = []
    total = None
    completion_seen = False
    try:
        for line in output.splitlines():
            if marker not in line:
                continue
            message = json.loads(line.split(marker, 1)[1])
            payload = message.get("Payload")
            if message["MessageType"] == "TestDiscovery.Completed":
                if (not isinstance(payload, dict) or type(payload.get("TotalTests")) is not int
                        or payload["TotalTests"] < 0 or payload.get("IsAborted", False) or completion_seen):
                    raise ValueError("aborted or repeated completion")
                completion_seen = True
                total = payload["TotalTests"]
                tests.extend(payload.get("LastDiscoveredTests") or [])
            elif message["MessageType"] == "TestDiscovery.TestFound":
                tests.extend(payload)
        names = [test["DisplayName"] for test in tests]
        ids = [test["Id"] for test in tests]
        if (total != len(expected) or sorted(names) != sorted(expected)
                or not all(ids) or len(ids) != len(set(ids))):
            raise ValueError("discovered executor identities do not match exact rows")
        return dict(zip(names, ids))
    except (KeyError, TypeError, ValueError) as error:
        raise VerificationError(f"DiscoveryIdentity: {error}") from error

ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'tests/TurboTest.Tests/TurboTest.Tests.csproj'
FILTERS = {'fast': 'Category!=PostgreSQL', 'slow': 'Category=PostgreSQL', 'all': None}
DOCKER_HOST = 'unix:///var/run/docker.sock'


def source_identity():
    import hashlib
    result = subprocess.run(['git', 'ls-files', '-z', '--cached', '--others', '--exclude-standard'],
                            check=True, capture_output=True).stdout
    paths = sorted(set(result.decode().split('\0')) - {''})
    files = {}
    for name in paths:
        if name.startswith(('src/', 'tests/', 'benchmarks/', 'examples/', 'scripts/', '.github/', '.config/')) or name in ('global.json', 'Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props', 'NuGet.Config', 'nuget.config', 'TurboTest.sln'):
            path = Path(name)
            if path.is_file() and '__pycache__' not in path.parts and path.suffix != '.pyc':
                files[name] = hashlib.sha256(path.read_bytes()).hexdigest()
    assets = {str(p): hashlib.sha256(p.read_bytes()).hexdigest()
              for root in ('src', 'tests', 'benchmarks', 'examples')
              for p in Path(root).glob('*/obj/project.assets.json')}
    binaries = {}
    # The executor loads project references and runtime dependencies from this tree.
    for path in Path('tests/TurboTest.Tests/bin/Release/net10.0').rglob('*'):
        if path.is_file() and (path.suffix == '.dll' or path.name.endswith(('.deps.json', '.runtimeconfig.json'))):
            binaries[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    return {'files': files, 'resolvedDependencies': assets, 'compiledInputs': binaries}


def docker_inventory(out, phase, env):
    output = run_phase(phase, ['docker', '--host', DOCKER_HOST, 'ps', '--all',
                              '--no-trunc', '--filter', 'label=turbotest.reservation',
                              '--format', '{{json .}}'], out, 30, env)
    rows = [json.loads(line) for line in output.splitlines() if line.strip()]
    write_json(out / (phase + '-containers.json'), rows)
    return {row['ID'] for row in rows}


def verify(args):
    import platform
    import uuid
    os.chdir(ROOT)
    if platform.system() != 'Linux':
        raise VerificationError('Platform: maintained verification currently requires Linux')
    out = Path(args.output).resolve()
    out.mkdir(parents=True, exist_ok=False)
    receipt = {'lane': args.lane, 'result': 'FAIL', 'startedUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
               'platform': platform.platform(), 'runId': str(uuid.uuid4()), 'dockerHost': DOCKER_HOST,
               'hostedWorkflow': ('running: ' + os.environ.get('GITHUB_RUN_ID', 'unknown')
                                  if os.environ.get('GITHUB_ACTIONS') == 'true' else 'not performed by local driver')}
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1',
               DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               VSLANG='1033', DOTNET_CLI_UI_LANGUAGE='en-US')
    before = None
    start = time.monotonic()
    try:
        if args.lane != 'fast':
            if env.get('DOCKER_HOST', DOCKER_HOST) != DOCKER_HOST or env.get('DOCKER_TLS_VERIFY') or env.get('DOCKER_CERT_PATH'):
                raise VerificationError('DockerConfiguration: native contracts require unix:///var/run/docker.sock without TLS variables')
            env['DOCKER_HOST'] = DOCKER_HOST
            run_phase('docker-info', ['docker', '--host', DOCKER_HOST, 'version', '--format', '{{json .Server}}'], out, 30, env)
            run_phase('postgres-image', ['docker', '--host', DOCKER_HOST, 'image', 'inspect', 'postgres:16-alpine',
                                         '--format', '{{.Id}}'], out, 30, env)
            before = docker_inventory(out, 'resources-before', env)
        before_build = source_identity()['files']
        run_phase('sdk', ['dotnet', '--info'], out, 30, env)
        run_phase('restore', ['dotnet', 'restore', 'TurboTest.sln'], out, 180, env)
        run_phase('build', ['dotnet', 'build', 'TurboTest.sln', '--no-restore', '-c', 'Release', '-nr:false', '-p:UseSharedCompilation=false'], out, 180, env)
        # This supported example is intentionally outside the solution.
        run_phase('example-build', ['dotnet', 'build', 'examples/DynamicScalingExample/DynamicScalingExample.csproj',
                                    '-c', 'Release', '-nr:false', '-p:UseSharedCompilation=false'], out, 180, env)
        binding = source_identity()
        if before_build != binding['files']:
            raise VerificationError('SourceChanged: compiler/test inputs changed during build')
        write_json(out / 'source-identity.json', binding)
        lanes = {}
        for lane, selection in FILTERS.items():
            command = ['dotnet', 'test', PROJECT, '--no-build', '-c', 'Release', '--list-tests']
            if lane == 'all':
                command += ['--diag', str(out / 'discovery-diagnostic.log')]
            if selection:
                command += ['--filter', selection]
            lanes[lane] = discovered_tests(run_phase('discover-' + lane, command, out, 60, env))
        lanes = reconcile_lanes(lanes['all'], lanes['fast'], lanes['slow'])
        identities = discovery_ids((out / 'discovery-diagnostic.log').read_text(), lanes['all'])
        write_json(out / 'test-catalog.json', {lane: [{'name': name, 'id': identities[name]} for name in rows]
                                              for lane, rows in lanes.items()})
        receipt['discovered'] = {lane: len(rows) for lane, rows in lanes.items()}
        selected = ['fast', 'slow'] if args.lane == 'all' else [args.lane]
        receipt['tests'] = {}
        for lane in selected:
            results = out / lane
            command = ['dotnet', 'test', PROJECT, '--no-build', '-c', 'Release', '--filter', FILTERS[lane],
                       '--results-directory', str(results), '--logger', 'trx;LogFileName=results.trx',
                       '--logger', 'console;verbosity=normal', '--blame-hang-timeout', '60s',
                       '--blame-hang-dump-type', 'none']
            # Keep raw TRX even when the runner returns failure.
            run_phase('test-' + lane, command, out, 120 if lane == 'fast' else 900, env)
            rows = read_results(results / 'results.trx', lanes[lane])
            if any(row['id'] != identities[row['name']] for row in rows):
                raise VerificationError('TestResults: executed identity differs from discovered identity')
            write_json(out / (lane + '-results.json'), rows)
            receipt['tests'][lane] = {'passed': len(rows), 'failed': 0, 'skipped': 0,
                                      'testSeconds': sum(row['seconds'] for row in rows)}
        if binding != source_identity():
            raise VerificationError('SourceChanged: compiler/test/dependency inputs changed during verification')
        receipt['result'] = 'PASS'
    except (VerificationError, OSError, subprocess.SubprocessError) as error:
        receipt['error'] = str(error)
    finally:
        if before is not None:
            try:
                after = docker_inventory(out, 'resources-after', env)
                receipt['resourceSettlement'] = {'before': sorted(before), 'after': sorted(after),
                    'added': sorted(after - before), 'removed': sorted(before - after),
                    'automaticDeletion': False}
                if after != before:
                    receipt['result'] = 'FAIL'
                    receipt['cleanupError'] = 'ResourceSettlement: labelled container inventory changed; inspect exact IDs; no unproved ownership deletion'
            except (VerificationError, OSError) as error:
                receipt['result'] = 'FAIL'
                receipt['cleanupError'] = str(error)
        receipt['elapsedSeconds'] = time.monotonic() - start
        write_json(out / 'verification.json', receipt)
    print(json.dumps(receipt))
    return 0 if receipt['result'] == 'PASS' else 1


def cancelled(signum, frame):
    raise VerificationError(f"ProcessCancelled: signal {signum}")


def main():
    signal.signal(signal.SIGTERM, cancelled)
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('lane', choices=FILTERS)
    parser.add_argument('--output', required=True, help='New evidence directory (never overwrite a prior run)')
    args = parser.parse_args()
    try:
        return verify(args)
    except (VerificationError, OSError) as error:
        parser.exit(1, str(error) + '\n')


if __name__ == '__main__':
    raise SystemExit(main())

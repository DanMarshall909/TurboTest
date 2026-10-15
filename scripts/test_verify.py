"""Behavior contracts for the maintained correctness driver (TT-029)."""
import unittest
import tempfile
import json
import sys
import signal
import subprocess
import time
from pathlib import Path
import verify


class LaneContracts(unittest.TestCase):
    def test_parameterized_rows_are_preserved_and_union_is_exact(self):
        rows = ['TurboTest.Tests.Rule.accepts(value: 0)', 'TurboTest.Tests.Rule.accepts(value: 1)']
        self.assertEqual(rows, verify.discovered_tests('\n'.join('    ' + r for r in rows)))
        self.assertEqual({'fast': [rows[0]], 'slow': [rows[1]], 'all': rows},
                         verify.reconcile_lanes(rows, [rows[0]], [rows[1]]))

    def test_missing_overlapping_duplicate_or_empty_discovery_is_rejected(self):
        cases = [([], [], []), (['a', 'b'], ['a'], []), (['a'], ['a'], ['a']),
                 (['a', 'a'], ['a'], []), (['a'], ['a', 'a'], []), (['a'], ['b'], [])]
        for all_rows, fast, slow in cases:
            with self.subTest(all=all_rows, fast=fast, slow=slow):
                with self.assertRaisesRegex(verify.VerificationError, 'LanePartition'):
                    verify.reconcile_lanes(all_rows, fast, slow)


class DiscoveryIdentityContracts(unittest.TestCase):
    def test_discovery_records_bind_each_parameterized_row_to_executor_identity(self):
        tests = [{'Id': 'id-zero', 'DisplayName': 'rule(0)'}, {'Id': 'id-one', 'DisplayName': 'rule(1)'}]
        message = {'MessageType': 'TestDiscovery.Completed', 'Payload': {'TotalTests': 2, 'LastDiscoveredTests': tests}}
        log = 'trace TestRequestSender.OnDiscoveryMessageReceived: Received message: ' + json.dumps(message)
        self.assertEqual({'rule(0)': 'id-zero', 'rule(1)': 'id-one'}, verify.discovery_ids(log, ['rule(0)', 'rule(1)']))
        with self.assertRaisesRegex(verify.VerificationError, 'DiscoveryIdentity'):
            verify.discovery_ids(log, ['rule(0)'])
        malformed = dict(message, Payload={'TotalTests': None, 'LastDiscoveredTests': []})
        prefix = 'trace TestRequestSender.OnDiscoveryMessageReceived: Received message: '
        with self.assertRaisesRegex(verify.VerificationError, 'DiscoveryIdentity'):
            verify.discovery_ids(prefix + json.dumps(malformed) + '\n' + log, ['rule(0)', 'rule(1)'])
        with self.assertRaisesRegex(verify.VerificationError, 'DiscoveryIdentity'):
            verify.discovery_ids(prefix + json.dumps(dict(message, Payload=None)), ['rule(0)', 'rule(1)'])
        tests[1]['Id'] = tests[0]['Id']
        with self.assertRaisesRegex(verify.VerificationError, 'DiscoveryIdentity'):
            verify.discovery_ids('trace TestRequestSender.OnDiscoveryMessageReceived: Received message: ' + json.dumps(message), ['rule(0)', 'rule(1)'])

class ResultContracts(unittest.TestCase):
    def write_trx(self, directory, outcomes):
        path = Path(directory) / 'run.trx'
        rows = ''.join(f'<UnitTestResult testId="{i}" testName="{name}" outcome="{outcome}" duration="00:00:01.250" />'
                       for i, (name, outcome) in enumerate(outcomes))
        count = len(outcomes)
        path.write_text('<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>' + rows +
                        f'</Results><ResultSummary outcome="Completed"><Counters total="{count}" executed="{count}" passed="{count}" failed="0" /></ResultSummary></TestRun>')
        return path

    def test_records_exact_test_id_row_duration_and_outcome(self):
        with tempfile.TemporaryDirectory() as directory:
            rows = verify.read_results(self.write_trx(directory, [('a(0)', 'Passed'), ('a(1)', 'Passed')]), ['a(0)', 'a(1)'])
            self.assertEqual(['a(0)', 'a(1)'], [row['name'] for row in rows])
            self.assertEqual(['0', '1'], [row['id'] for row in rows])
            self.assertEqual([1.25, 1.25], [row['seconds'] for row in rows])

    def test_failed_skipped_missing_duplicate_and_unexpected_results_cannot_pass(self):
        cases = [([('a', 'Failed')], ['a']), ([('a', 'NotExecuted')], ['a']),
                 ([], ['a']), ([('a', 'Passed'), ('a', 'Passed')], ['a']),
                 ([('b', 'Passed')], ['a'])]
        for outcomes, expected in cases:
            with self.subTest(outcomes=outcomes), tempfile.TemporaryDirectory() as directory:
                with self.assertRaisesRegex(verify.VerificationError, 'TestResults'):
                    verify.read_results(self.write_trx(directory, outcomes), expected)

    def test_run_errors_bad_ids_and_invalid_durations_are_rejected(self):
        changes = [('outcome="Completed"', 'outcome="Aborted"'),
                   ('http://microsoft.com/schemas/VisualStudio/TeamTest/2010', 'urn:unexpected'),
                   ('</TestRun>', '<ResultSummary outcome="Failed"><Counters failed="1" /></ResultSummary></TestRun>'),
                   ('passed="1"', 'passed="0"'), ('testId="0"', 'testId=""'),
                   ('00:00:01.250', '00:00:NaN'), ('00:00:01.250', '-1:00:00'),
                   ('</ResultSummary>', '<RunInfos><RunInfo outcome="Error" /></RunInfos></ResultSummary>')]
        for old, new in changes:
            with self.subTest(change=new), tempfile.TemporaryDirectory() as directory:
                path = self.write_trx(directory, [('a', 'Passed')])
                path.write_text(path.read_text().replace(old, new))
                with self.assertRaisesRegex(verify.VerificationError, 'TestResults'):
                    verify.read_results(path, ['a'])
        with tempfile.TemporaryDirectory() as directory:
            path = self.write_trx(directory, [('a', 'Passed'), ('b', 'Passed')])
            path.write_text(path.read_text().replace('testId="1"', 'testId="0"'))
            with self.assertRaisesRegex(verify.VerificationError, 'TestResults'):
                verify.read_results(path, ['a', 'b'])


class ProcessContracts(unittest.TestCase):
    def test_success_and_failure_retain_command_exit_output_and_duration(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            command = [sys.executable, '-c', 'print("phase-result")']
            verify.run_phase('success', command, out, 5)
            receipt = json.loads((out / 'success.json').read_text())
            self.assertEqual(command, receipt['command'])
            self.assertEqual(0, receipt['exitCode'])
            self.assertGreater(receipt['elapsedSeconds'], 0)
            self.assertIn('phase-result', (out / 'success.log').read_text())
            with self.assertRaisesRegex(verify.VerificationError, 'PhaseFailed: failure'):
                verify.run_phase('failure', [sys.executable, '-c', 'raise SystemExit(7)'], out, 5)
            self.assertEqual(7, json.loads((out / 'failure.json').read_text())['exitCode'])

    def test_deadline_stops_owned_descendants_and_retains_stalled_phase(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            pidfile = out / 'child.pid'
            code = ('import subprocess,sys,time; from pathlib import Path; '
                    'p=subprocess.Popen([sys.executable,"-c","import time; time.sleep(30)"]); '
                    f'Path({str(pidfile)!r}).write_text(str(p.pid)); time.sleep(30)')
            with self.assertRaisesRegex(verify.VerificationError, 'PhaseDeadline: stalled'):
                verify.run_phase('stalled', [sys.executable, '-c', code], out, .4)
            pid = int(pidfile.read_text())
            status = Path(f'/proc/{pid}/stat')
            self.assertTrue(not status.exists() or status.read_text().split()[2] == 'Z')
            receipt = json.loads((out / 'stalled.json').read_text())
            self.assertTrue(receipt['deadlineExceeded'])
            self.assertTrue(receipt['processGroupStopped'])

    def test_successful_parent_cannot_leave_live_owned_children(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            code = 'import subprocess,sys; subprocess.Popen([sys.executable,"-c","import time; time.sleep(30)"])'
            with self.assertRaisesRegex(verify.VerificationError, 'PhaseWorkers: orphan'):
                verify.run_phase('orphan', [sys.executable, '-c', code], out, 5)
            receipt = json.loads((out / 'orphan.json').read_text())
            self.assertEqual(0, receipt['exitCode'])
            self.assertTrue(receipt['processGroupStopped'])

    def test_signal_cancellation_settles_phase_and_preserves_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory)
            ready = out / 'ready'
            child = f'from pathlib import Path; import time; Path({str(ready)!r}).touch(); time.sleep(30)'
            code = ('import sys; from pathlib import Path; import verify; '
                    f'verify.verify=lambda args: verify.run_phase("cancelled",[sys.executable,"-c",{child!r}],Path({str(out)!r}),10); '
                    'sys.argv=["verify.py","fast","--output","unused"]; verify.main()')
            process = subprocess.Popen([sys.executable, '-c', code], cwd=Path(verify.__file__).parent,
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            try:
                until = time.monotonic() + 5
                while not ready.exists() and time.monotonic() < until:
                    time.sleep(.01)
                self.assertTrue(ready.exists())
                process.send_signal(signal.SIGTERM)
                self.assertNotEqual(0, process.wait(timeout=5))
                receipt = json.loads((out / 'cancelled.json').read_text())
                self.assertTrue(receipt['processGroupStopped'])
                self.assertFalse(verify.group_members(receipt['processGroupId']))
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    unittest.main()

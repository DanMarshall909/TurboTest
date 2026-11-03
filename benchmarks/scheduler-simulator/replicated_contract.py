"""Slow synthetic CLI boundaries for the replicated whole-path protocol."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from test_replicated import cohort
from test_revised import trace_fixture


class ReplicatedCliContract(unittest.TestCase):
    def test_fresh_outputs_freeze_forecasts_and_distinguish_failed_gate_from_usage_error(self):
        program=Path(__file__).with_name('replicated.py')
        with tempfile.TemporaryDirectory(prefix='tt039-r4-cli-') as temp:
            root=Path(temp);contract,write=cohort(root)
            def run(action,output,*extra):
                return subprocess.run([sys.executable,str(program),action,'--trace-root',str(root),
                    '--output',str(root/output),*extra],capture_output=True,text=True,timeout=30)
            missing=run('validate','evaluation.json')
            self.assertEqual(2,missing.returncode)
            self.assertIn('requires --fit and --forecast',missing.stderr)
            self.assertFalse((root/'evaluation.json').exists())
            fitted=run('fit','fit.json');self.assertEqual(0,fitted.returncode,fitted.stderr)
            original=(root/'fit.json').read_bytes()
            duplicate=run('fit','fit.json');self.assertEqual(1,duplicate.returncode)
            self.assertIn('ReplicatedInput:',duplicate.stderr)
            self.assertEqual(original,(root/'fit.json').read_bytes())
            args=('--fit',str(root/'fit.json'),'--forecast',str(root/'forecast.json'))
            frozen=run('forecast','forecast.json',*args[:2]);self.assertEqual(0,frozen.returncode,frozen.stderr)
            incomplete=run('validate','evaluation.json',*args)
            self.assertEqual(1,incomplete.returncode);self.assertFalse((root/'evaluation.json').exists())
            for row in contract['cases']:
                if row['split']!='validation':continue
                write(row['name']+'/report.json',trace_fixture(row['mode']))
                write(row['name']+'/process.json',dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
            result=run('validate','evaluation.json',*args)
            self.assertEqual(0,result.returncode,result.stderr)
            evaluation=json.loads((root/'evaluation.json').read_text())
            self.assertTrue(evaluation['gate']['qualified'])
            self.assertEqual((4,14),(len(evaluation['gate']['initialTwo']['perRunSuite']),len(evaluation['gate']['fullCohort']['perRunSuite'])))
            for row in contract['cases']:
                if row['split']!='validation':continue
                report=trace_fixture(row['mode'])
                for event in report['Boundaries']:event['Tick']*=10
                for sample in report['Samples']:
                    for key in ('Started','Acquired','Returning','Returned'):sample[key]*=10
                for key in ('SuiteMilliseconds','FinalResetsFinishedMilliseconds','LastTestFinishedMilliseconds'):report[key]*=10
                write(row['name']+'/report.json',report)
            failed=run('validate','negative.json',*args)
            self.assertEqual(2,failed.returncode,failed.stderr)
            self.assertFalse(json.loads((root/'negative.json').read_text())['gate']['qualified'])

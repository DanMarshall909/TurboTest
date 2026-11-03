import importlib.util
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

PROGRAM=Path('experiments/tt039/collect_r4.py').resolve()
spec=importlib.util.spec_from_file_location('r4_collector',PROGRAM)
collector=importlib.util.module_from_spec(spec);spec.loader.exec_module(collector)


class FixedCollectionContract(unittest.TestCase):
    def test_withdrawal_between_cases_retains_an_incomplete_attempt(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            cases=[dict(name=f'case-{n}',mode='awaited',waitMilliseconds=0) for n in range(2)]
            class Driver:
                calls=[]
                def run_case(self,*args):self.calls.append(args[0]);(root/'STOP_PERFORMANCE').touch()
            driver=Driver()
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation.*withdrawn'):
                collector.collect_cases(root,cases,driver,clock=lambda:0,authorize=lambda:None)
            self.assertEqual(['case-0'],driver.calls)
            self.assertFalse(json.loads((root/'collection-progress.json').read_text())['complete'])

    def test_budget_expiry_leaves_incomplete_cohort_without_launching_more_cases(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            cases=[dict(name=f'case-{n}',mode='awaited',waitMilliseconds=0) for n in range(3)]
            class Driver:
                calls=[]
                def run_case(self,*args):self.calls.append(args[0]);return {'predictiveGate':False}
            driver=Driver();times=iter([0,0,7300,7300])
            with self.assertRaisesRegex(TimeoutError,'CollectionBudget'):
                collector.collect_cases(root,cases,driver,clock=lambda:next(times),authorize=lambda:None)
            self.assertEqual(['case-0'],driver.calls)
            state=json.loads((root/'collection-progress.json').read_text())
            self.assertFalse(state['complete'])
            self.assertEqual(['case-0'],state['completed'])
            self.assertEqual(3,state['expected'])

    def test_fixed_cohort_finishes_despite_prediction_misses_and_rejects_existing_cases(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            cases=[dict(name=f'case-{n}',mode='background',waitMilliseconds=25) for n in range(3)]
            class Driver:
                calls=[]
                def run_case(self,*args):
                    self.calls.append(args[0]);(root/args[0]).mkdir()
                    return {'predictiveGate':False}
            driver=Driver()
            collector.collect_cases(root,cases,driver,clock=lambda:0,authorize=lambda:None)
            state=json.loads((root/'collection-progress.json').read_text())
            self.assertTrue(state['complete']);self.assertEqual(3,len(driver.calls))
            with self.assertRaisesRegex(FileExistsError,'CollectionExisting'):
                collector.collect_cases(root,cases,driver,clock=lambda:0,authorize=lambda:None)
            self.assertEqual(3,len(driver.calls))


class MeasurementConfirmationContract(unittest.TestCase):
    def test_absent_stale_or_withdrawn_confirmation_cannot_start_measurement(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp)
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation'):
                collector.require_measurement_window(root)
            write=lambda name,value:(root/name).write_text(json.dumps(value))
            write('protocol-freeze.json',{'checkpoint':'fixture-source'})
            window=dict(approvedBy='Dan',approvalReference='synthetic fixture; no native work',
                        sourceCheckpoint='fixture-source',scopes=['TT-038','TT-039'],validUntilUtc='2100-01-01T00:00:00+00:00')
            write('measurement-window.json',window)
            self.assertEqual(window,collector.require_measurement_window(root))
            self.assertEqual(window,collector.require_measurement_window(root,scope='TT-038'))
            write('measurement-window.json',window|dict(validUntilUtc='2000-01-01T00:00:00+00:00'))
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation.*expired'):
                collector.require_measurement_window(root)
            write('measurement-window.json',window|dict(sourceCheckpoint='different-source'))
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation.*source'):
                collector.require_measurement_window(root)
            write('measurement-window.json',window)
            (root/'STOP_PERFORMANCE').touch()
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation.*withdrawn'):
                collector.require_measurement_window(root)


class FreshNativePlanContract(unittest.TestCase):
    def test_old_data_or_different_workload_cannot_be_the_fresh_measurement_contract(self):
        import sys
        sys.path.insert(0,'benchmarks/scheduler-simulator')
        from replicated import calibration_cases,future_cases
        strata={(mode,wait) for mode in ('awaited','background') for wait in (0,25,100,500)}
        plan=dict(slots=8,workers=4,resetConcurrency=4,cycles=256,warmups=32,
                  collectionBudgetSeconds=7200,caseDeadlineSeconds=300,caseReserveSeconds=480,
                  cases=calibration_cases(strata)+future_cases(strata),
                  protocolSha256=hashlib.sha256(Path('benchmarks/scheduler-simulator/protocol-r4.md').read_bytes()).hexdigest())
        collector.check_contract(plan)
        bad=copy.deepcopy(plan);bad['cases'][0]['split']='validation'
        with self.assertRaisesRegex(ValueError,'CollectionContract.*56 fresh'):collector.check_contract(bad)
        with self.assertRaisesRegex(ValueError,'CollectionContract.*workload'):collector.check_contract(plan|dict(cycles=255))
        with self.assertRaisesRegex(ValueError,'CollectionContract.*protocol'):collector.check_contract(plan|dict(protocolSha256='wrong'))

"""Functional routing checks; fake drivers never launch native processes."""
import importlib.util
import json
from pathlib import Path
import tarfile
import tempfile
import types
import unittest
from unittest.mock import patch


def load(name,filename):
    spec=importlib.util.spec_from_file_location(name,Path('experiments/tt039')/filename)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    return module


class PerformanceEntrypointContract(unittest.TestCase):
    def test_tt039_refuses_before_loading_a_native_driver_when_confirmation_is_missing(self):
        collector=load('tt039_entrypoint','collect_r4.py')
        with (patch('sys.argv',['collect_r4.py','qualify']),
              patch.object(collector,'require_measurement_window',side_effect=PermissionError('PerformanceConfirmation')),
              patch.object(collector.shared,'native_driver') as native):
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation'):collector.main()
            native.assert_not_called()

    def test_tt039_controls_cover_every_timing_check_and_reconfirm_before_each_operation(self):
        collector=load('tt039_control_plan','collect_r4.py');events=[];cases=[];commands=[];deadlines=[]
        def approve(*args,**kwargs):events.append('approved');return {}
        def case(*args):events.append('native');cases.append(args)
        def phase(name,command,*args):events.append('native');commands.append((name,command))
        def control(driver,name,command,deadline):deadlines.append(deadline);phase(name,command)
        driver=types.SimpleNamespace(run_case=case,verify=types.SimpleNamespace(run_phase=phase),ENV={})
        with (tempfile.TemporaryDirectory() as temp,patch('sys.argv',['collect_r4.py','qualify']),
              patch.object(collector,'ROOT',Path(temp)),patch.object(collector,'require_measurement_window',side_effect=approve),
              patch.object(collector,'check_protocol_freeze'),patch.object(collector,'run_control',side_effect=control),
              patch.object(collector.shared,'native_driver',return_value=driver)):
            collector.main()
        self.assertEqual(10,len(cases));self.assertEqual(3,len(commands))
        self.assertEqual(['qualified-smoke-awaited','qualified-fault-awaited','qualified-smoke-background','qualified-fault-background'],[c[0] for c in cases[:4]])
        self.assertEqual(['overhead-r0-on','overhead-r0-off','overhead-r1-off','overhead-r1-on','overhead-r2-on','overhead-r2-off'],[c[0] for c in cases[4:]])
        self.assertEqual(['held-control','held-control','probe-cost'],[command[2] for _,command in commands])
        self.assertEqual([300,300,30],deadlines)
        self.assertTrue(all(i>0 and events[i-1]=='approved' for i,event in enumerate(events) if event=='native'))

    def test_tt038_analysis_checks_source_freeze_without_requiring_a_live_measurement_window(self):
        baseline=load('tt038_analysis','baseline_rerun.py')
        def drift(*args):raise ValueError('FreezeSource: changed analyzer')
        native=types.SimpleNamespace(verify=types.SimpleNamespace(run_phase=lambda *args:None))
        session=types.SimpleNamespace(check_protocol_freeze=drift,
                                     shared=types.SimpleNamespace(native_driver=lambda *args:native))
        with (tempfile.TemporaryDirectory() as temp,patch('sys.argv',['baseline_rerun.py','analyze']),
              patch.object(baseline,'TASK',Path(temp)),patch.object(baseline,'session_driver',return_value=session)):
            with self.assertRaisesRegex(ValueError,'FreezeSource.*analyzer'):baseline.main()

    def test_tt038_analysis_requires_complete_progress_and_successful_outer_measurement(self):
        baseline=load('tt038_analysis_settlement','baseline_rerun.py');launched=[]
        native=types.SimpleNamespace(verify=types.SimpleNamespace(run_phase=lambda *args:launched.append(args)))
        session=types.SimpleNamespace(check_protocol_freeze=lambda *args:None,
                                     shared=types.SimpleNamespace(native_driver=lambda *args:native))
        with (tempfile.TemporaryDirectory() as temp,patch('sys.argv',['baseline_rerun.py','analyze']),
              patch.object(baseline,'TASK',Path(temp)),patch.object(baseline,'ROOT',Path(temp)/'v4'),
              patch.object(baseline,'session_driver',return_value=session)):
            root=Path(temp)/'v4';root.mkdir()
            write=lambda name,data:(root/name).write_text(json.dumps(data))
            write('contract.json',dict(cases=[dict(name='first'),dict(name='last')]))
            correct=dict(phase='baseline',complete=True,expected=2,completed=['first','last'],failure=None)
            receipt=dict(command=['python3','experiments/tt039/baseline_rerun.py','measure'],
                         exitCode=0,processGroupStopped=True,deadlineExceeded=False)
            write('baseline-progress.json',correct|dict(complete=False,completed=['first'],failure='ResourceSettlement'))
            write('baseline-process.json',receipt)
            with self.assertRaisesRegex(ValueError,'BaselineSettlement'):baseline.main()
            write('baseline-progress.json',correct);write('baseline-process.json',receipt|dict(exitCode=1))
            with self.assertRaisesRegex(ValueError,'BaselineSettlement'):baseline.main()
            self.assertEqual([],launched)
            write('baseline-process.json',receipt);baseline.main()
            self.assertEqual(1,len(launched))

    def test_tt038_refuses_before_loading_the_historical_driver_without_confirmation(self):
        baseline=load('tt038_entrypoint','baseline_rerun.py')
        def deny(*args,**kwargs):raise PermissionError('PerformanceConfirmation')
        session=types.SimpleNamespace(require_measurement_window=deny)
        with patch('sys.argv',['baseline_rerun.py','measure']),patch.object(baseline,'session_driver',return_value=session),patch.object(baseline,'native_driver') as native:
            with self.assertRaisesRegex(PermissionError,'PerformanceConfirmation'):baseline.main()
            native.assert_not_called()

    def test_tt038_routes_all_controls_and_forty_cases_with_per_case_confirmation(self):
        baseline=load('tt038_fixed_rerun','baseline_rerun.py')
        collector=load('tt038_fixed_collector','collect_r4.py')
        with tarfile.open('docs/issues/evidence/2026-11-02/tt038/raw-evidence.tar.gz') as archive:
            contract=json.load(archive.extractfile('v4/contract.json'))
        events=[];calls=[]
        def approve(*args,**kwargs):events.append('approved')
        def case(*args):events.append('native');calls.append(args)
        session=types.SimpleNamespace(require_measurement_window=approve,check_protocol_freeze=lambda *args:None,
                                     collect_cases=collector.collect_cases)
        driver=types.SimpleNamespace(run_case=case)
        with (tempfile.TemporaryDirectory() as temp,patch.object(baseline,'ROOT',Path(temp)),
              patch.object(baseline,'session_driver',return_value=session),patch.object(baseline,'native_driver',return_value=driver)):
            root=Path(temp)
            with patch('sys.argv',['baseline_rerun.py','qualify']):baseline.main()
            self.assertEqual([('smoke-awaited','awaited',0,16,8),('fault-awaited','awaited',0,8,4,'distribution'),
                              ('smoke-background','background',0,16,8),('fault-background','background',0,8,4,'distribution')],calls)
            (root/'contract.json').write_text(json.dumps(contract))
            receipt=dict(exitCode=1,processGroupStopped=True,deadlineExceeded=False)
            (root/'qualification.json').write_text(json.dumps(receipt))
            with patch('sys.argv',['baseline_rerun.py','measure']):
                with self.assertRaisesRegex(ValueError,'BaselineQualification'):baseline.main()
                self.assertEqual(4,len(calls))
                receipt['exitCode']=0;(root/'qualification.json').write_text(json.dumps(receipt))
                baseline.main()
            self.assertEqual(40,len(calls[4:]))
            self.assertEqual([(r['name'],r['mode'],r['waitMilliseconds'],256,32) for r in contract['cases']],calls[4:])
            self.assertTrue(json.loads((root/'baseline-progress.json').read_text())['complete'])
        self.assertTrue(all(i>0 and events[i-1]=='approved' for i,event in enumerate(events) if event=='native'))

import copy
import json
import importlib.util
from pathlib import Path
import shutil
import tempfile
import unittest

from replicated import forecast,assess_cohorts,fit,calibration_cases,future_cases,validate,check_collection
from reference import latency_metrics
from test_revised import trace_fixture


def complete_phase(contract,write,phase):
    names=[r['name'] for r in contract['cases'] if r['split']==phase]
    write(phase+'-progress.json',dict(phase=phase,expected=len(names),completed=names,complete=True,failure=None))
    action='calibrate' if phase=='calibration' else 'validate'
    write(phase+'-process.json',dict(command=['python3','experiments/tt039/collect_r4.py',action],
          exitCode=0,processGroupStopped=True,deadlineExceeded=False))


def cohort(root):
    def write(name,value):
        path=root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(value))
    for name in ('image.json','runtime-identity.json'):write(name,{'fixture':name})
    write('source-identity.json',{'sampler':'r4-fixture'})
    write('measurement-window.json',{'purpose':'synthetic functional test; no native measurements'})
    for name in ('protocol-freeze.json','collection-freeze.json'):write(name,{'purpose':'synthetic functional test'})
    strata={(mode,0) for mode in ('awaited','background')}
    contract=dict(slots=1,workers=1,resetConcurrency=1,cycles=2,
                  cases=calibration_cases(strata)+future_cases(strata))
    for row in contract['cases']:
        if row['split']=='calibration':
            write(row['name']+'/report.json',trace_fixture(row['mode']))
            write(row['name']+'/process.json',dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
    write('contract.json',contract)
    complete_phase(contract,write,'calibration')
    return contract,write


class ExactPathEnsembleContract(unittest.TestCase):
    def test_expectation_enumerates_every_whole_path_without_hybrid_records(self):
        footer=dict(checkout_us=0,handoff_us=0,receipt_us=0,publication_us=0,
                    release_us=0,completion_us=0,dispatch_us=0)
        runs=[]
        for name,scale in (("a",1),("b",2),("c",10)):
            streams=[[(1,10),(3,7)],[(5,1),(4,2)]]
            workers=[[footer|dict(body_us=body*1000*scale,reset_us=reset*1000*scale)
                      for body,reset in stream] for stream in streams]
            runs.append(dict(name=name,workers=workers,initial_us=[0,2000*scale]))
        fitted=dict(settings=dict(slots=2,workers=2,resetConcurrency=1,cycles=4),
                    groups={"background/0":runs,"awaited/0":runs})
        original=copy.deepcopy(fitted)
        result=forecast(fitted)
        self.assertEqual(104,result["predictions"]["background/0"]["suite_ms"])
        self.assertAlmostEqual(299/3,result["predictions"]["awaited/0"]["suite_ms"])
        self.assertEqual([24,48,240],[p["suite_ms"] for p in result["pathMetrics"]["background/0"]])
        self.assertEqual({"background/0":3,"awaited/0":3},result["trainingPathCounts"])
        self.assertEqual(6,len(result["sensitivities"]))
        self.assertIn("expectation",result["pointRule"])
        self.assertEqual(original,fitted)
        self.assertEqual(result,forecast(fitted))


class OriginalChallengeContract(unittest.TestCase):
    def test_later_visits_cannot_rescue_a_failed_original_two_visit_challenge(self):
        predicted=latency_metrics(100,[0],[0],0)|dict(clean_starvation=0,clean_queue_mean_ms=0)
        rows=[]
        for visit in range(7):
            observed=predicted|dict(suite_ms=115 if visit<2 else 100)
            rows.append(dict(name=f'visit-{visit}',mode='awaited',waitMilliseconds=0,
                             observed=observed,predicted=predicted))
        gate=assess_cohorts(rows)
        self.assertFalse(gate['initialTwo']['qualified'])
        self.assertTrue(gate['fullCohort']['qualified'])
        self.assertFalse(gate['qualified'])
        self.assertEqual(['visit-0','visit-1'],gate['initialCaseNames'])
        self.assertTrue(all(r['passed'] for r in gate['fullCohort']['perRunSuite']))
        rows[0]['observed']=rows[1]['observed']=predicted.copy()
        self.assertTrue(assess_cohorts(rows)['qualified'])


class ReplicatedTrainingContract(unittest.TestCase):
    def test_phase_proof_requires_exact_order_count_identity_and_stopped_success(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            for phase in ('calibration','validation'):
                complete_phase(contract,write,phase)
                progress=json.loads((root/(phase+'-progress.json')).read_text())
                receipt=json.loads((root/(phase+'-process.json')).read_text())
                check_collection(root,contract,phase)
                for changed in (dict(completed=progress['completed'][::-1]),dict(expected=999),
                                dict(phase='wrong'),dict(failure='ResourceSettlement'),dict(complete=False)):
                    write(phase+'-progress.json',progress|changed)
                    with self.assertRaisesRegex(ValueError,'CollectionSettlement.*'+phase):check_collection(root,contract,phase)
                write(phase+'-progress.json',progress)
                for changed in (dict(exitCode=1),dict(processGroupStopped=False),dict(deadlineExceeded=True)):
                    write(phase+'-process.json',receipt|changed)
                    with self.assertRaisesRegex(ValueError,'CollectionSettlement.*'+phase):check_collection(root,contract,phase)

    def test_last_case_cleanup_failure_and_outer_freeze_failure_cannot_fit_or_qualify(self):
        spec=importlib.util.spec_from_file_location('collection_settlement',Path('experiments/tt039/collect_r4.py'))
        collector=importlib.util.module_from_spec(spec);spec.loader.exec_module(collector)
        for phase in ('calibration','validation'):
            with self.subTest(phase=phase),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);contract,write=cohort(root)
                fitted=fit(root,contract);frozen=forecast(fitted)
                selected=[r for r in contract['cases'] if r['split']==phase]
                if phase=='calibration':
                    for row in selected:shutil.rmtree(root/row['name'])
                def case(name,mode,*args):
                    write(name+'/report.json',trace_fixture(mode))
                    write(name+'/process.json',dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
                    if name==selected[-1]['name']:raise ValueError('ResourceSettlement')
                class Driver:run_case=staticmethod(case)
                complete_phase(contract,write,phase)
                with self.assertRaisesRegex(ValueError,'ResourceSettlement'):
                    collector.collect_cases(root,selected,Driver(),clock=lambda:0,authorize=lambda:None,phase=phase)
                # All successful child reports exist, but the last case never settled.
                evaluate=lambda:fit(root,contract) if phase=='calibration' else validate(root,contract,fitted,frozen)
                with self.assertRaisesRegex(ValueError,'CollectionSettlement.*'+phase):evaluate()
                complete_phase(contract,write,phase)
                receipt=json.loads((root/(phase+'-process.json')).read_text())
                write(phase+'-process.json',receipt|dict(exitCode=1))
                # Complete progress cannot hide the final post-collection freeze failure.
                with self.assertRaisesRegex(ValueError,'CollectionSettlement.*'+phase):evaluate()
                complete_phase(contract,write,phase)
                write(phase+'-process.json',receipt|dict(command=['python3','experiments/tt039/collect_r4.py','qualify']))
                with self.assertRaisesRegex(ValueError,'CollectionSettlement.*'+phase):evaluate()

    def test_calibration_cannot_silently_drop_a_mode_or_fit_an_empty_cohort(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            for rows in ([],[r for r in contract['cases'] if r['mode']=='awaited']):
                altered=contract|dict(cases=rows);write('contract.json',altered)
                with self.assertRaisesRegex(ValueError,'CalibrationSplit.*both modes'):
                    fit(root,altered)

    def test_only_fresh_calibration_paths_train_without_reading_history_or_validation(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            write('historical/contract.json',{'mustNotBeRead':True})
            original=(root/'historical/contract.json').read_bytes()
            fitted=fit(root,contract)
            self.assertTrue({'calibration-progress.json','calibration-process.json'}<=fitted['trainingFiles'].keys())
            self.assertEqual(14,len(fitted['calibrationRuns']))
            self.assertEqual({'awaited/0':7,'background/0':7},{k:len(v) for k,v in fitted['groups'].items()})
            self.assertEqual(14,sum(r['split']=='validation' for r in contract['cases']))
            for row in contract['cases']:
                if row['split']=='validation':write(row['name']+'/report.json',{'mustNotBeRead':True})
            self.assertEqual(fitted,fit(root,contract))
            self.assertEqual(original,(root/'historical/contract.json').read_bytes())
            self.assertFalse(any(n.startswith('historical/') or n.startswith('training/') for n in fitted['trainingFiles']))
            altered=copy.deepcopy(contract);altered['cases'][0]['split']='validation'
            write('contract.json',altered)
            with self.assertRaisesRegex(ValueError,'CalibrationSplit'):fit(root,altered)

    def test_full_native_plan_is_fixed_balanced_and_contains_every_required_visit(self):
        strata={(mode,wait) for mode in ('awaited','background') for wait in (0,25,100,500)}
        plan=future_cases(strata)
        calibration=calibration_cases(strata)
        self.assertEqual(56,len(calibration))
        self.assertEqual(['r0-w0-awaited','r0-w0-background','r0-w25-awaited','r0-w25-background'],
                         [r['name'] for r in calibration[:4]])
        self.assertEqual(84,len(plan))
        self.assertEqual(['r7-w25-awaited','r7-w25-background','r7-w0-background','r7-w0-awaited'],
                         [r['name'] for r in plan[:4]])
        self.assertEqual(84,len({r['name'] for r in plan}))
        for mode,wait in strata:
            self.assertEqual(7,sum((r['mode'],r['waitMilliseconds'])==(mode,wait) for r in calibration))
            selected=[r for r in plan if (r['mode'],r['waitMilliseconds'])==(mode,wait)]
            self.assertEqual(21 if wait==25 else 7,len(selected))
            self.assertEqual(7,selected[0]['pair'] if wait in (0,25) else selected[0]['pair']-(1 if wait==100 else 2))

    def test_frozen_predictions_reject_future_costs_and_edited_model_data(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            fitted=fit(root,contract);frozen=forecast(fitted)
            with self.assertRaises(OSError):validate(root,contract,fitted,frozen)
            for row in contract['cases']:
                if row['split']!='validation':continue
                report=trace_fixture(row['mode'])
                for event in report['Boundaries']:event['Tick']*=10
                for sample in report['Samples']:
                    for key in ('Started','Acquired','Returning','Returned'):sample[key]*=10
                for key in ('SuiteMilliseconds','FinalResetsFinishedMilliseconds','LastTestFinishedMilliseconds'):report[key]*=10
                write(row['name']+'/report.json',report)
                write(row['name']+'/process.json',dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
            complete_phase(contract,write,'validation')
            result=validate(root,contract,fitted,frozen)
            self.assertTrue({'validation-progress.json','validation-process.json'}<=result['validationFiles'].keys())
            self.assertFalse(result['gate']['qualified'])
            self.assertEqual((4,14),(len(result['gate']['initialTwo']['perRunSuite']),len(result['gate']['fullCohort']['perRunSuite'])))
            self.assertEqual(28,len(result['runs']))
            self.assertTrue(all(r['predicted']['suite_ms']<100 for r in result['runs']))
            changed=copy.deepcopy(fitted);changed['groups']['awaited/0'][0]['workers'][0][0]['body_us']+=1
            with self.assertRaisesRegex(ValueError,'FitIdentity'):validate(root,contract,changed,frozen)
            changed=copy.deepcopy(frozen);changed['predictions']['awaited/0']['suite_ms']=550
            with self.assertRaisesRegex(ValueError,'ForecastIdentity'):validate(root,contract,fitted,changed)

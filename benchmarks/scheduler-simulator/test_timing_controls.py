"""Synthetic subprocess seam checks; no timed native code or Docker is invoked."""
import importlib.util
import json
from pathlib import Path
import tempfile
import types
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('r4_control_wrapper',Path('experiments/tt039/collect_r4.py'))
collector=importlib.util.module_from_spec(spec);spec.loader.exec_module(collector)


def probe_command(root):
    payload=[dict(Enabled=e,Round=r,Count=50000,MicrosecondsPerEvent=1.0)
             for e in (False,True) for r in range(3)]
    path=root/'probe-cost.json';path.write_text(json.dumps(payload))
    return ['dotnet','fixture.dll','probe-cost',str(path)]


class TimingControlSettlementContract(unittest.TestCase):
    def test_controls_recheck_freeze_and_inventory_after_success_and_process_failure(self):
        for failed in (False,True):
            with self.subTest(failed=failed),tempfile.TemporaryDirectory() as temp:
                root=Path(temp);checks=[];inventories=iter([{'containers':[]},{'containers':['leak']} if failed else {'containers':[]}])
                def launch(*args):
                    if failed:raise RuntimeError('native failure')
                driver=types.SimpleNamespace(inventory=lambda:next(inventories),ENV={},
                                             verify=types.SimpleNamespace(run_phase=launch))
                with patch.object(collector,'ROOT',root),patch.object(collector,'check_protocol_freeze',side_effect=lambda:checks.append('freeze')):
                    if failed:
                        with self.assertRaisesRegex(ValueError,'ControlSettlement'):
                            collector.run_control(driver,'probe-cost-process',probe_command(root),30)
                    else:collector.run_control(driver,'probe-cost-process',probe_command(root),30)
                self.assertEqual(['freeze','freeze'],checks)
                settlement=json.loads((root/'probe-cost-process-settlement.json').read_text())
                self.assertEqual(not failed,settlement['unchanged'])
                self.assertEqual({'containers':[]},settlement['before'])

    def test_source_drift_after_a_successful_control_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            driver=types.SimpleNamespace(inventory=lambda:{},ENV={},verify=types.SimpleNamespace(run_phase=lambda *args:None))
            with (patch.object(collector,'ROOT',Path(temp)),
                  patch.object(collector,'check_protocol_freeze',side_effect=[None,ValueError('FreezeSource: changed runtime')])):
                with self.assertRaisesRegex(ValueError,'FreezeSource'):
                    collector.run_control(driver,'probe-cost-process',probe_command(Path(temp)),30)

    def test_success_exit_cannot_hide_wrong_held_behavior_or_missing_probe_rounds(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);held=root/'held';held.mkdir()
            correct=dict(Passed=True,Mode='awaited',HeldReceipt=True,CleanAlternate=True,
                         ExhaustedAcquisitionWaited=True,ShutdownJoinedReset=True)
            command=['dotnet','fixture.dll','held-control','awaited',str(held)]
            for key,value in [('Mode','background'),('HeldReceipt',False),('CleanAlternate',False),
                              ('ExhaustedAcquisitionWaited',False),('ShutdownJoinedReset',False),('Passed',False)]:
                with self.subTest(key=key):
                    (held/'control.json').write_text(json.dumps(correct|{key:value}))
                    with self.assertRaisesRegex(ValueError,'ControlResult.*held'):
                        collector.check_control_result(command)
            (held/'control.json').write_text(json.dumps(correct));collector.check_control_result(command)
            command=probe_command(root);path=root/'probe-cost.json';original=json.loads(path.read_text())
            collector.check_control_result(command)
            for payload in (original[:-1],original[:-1]+[original[0]],
                            [row|dict(Count=49999) for row in original],
                            [row|dict(MicrosecondsPerEvent=float('nan')) for row in original]):
                path.write_text(json.dumps(payload))
                with self.assertRaisesRegex(ValueError,'ControlResult.*probe'):
                    collector.check_control_result(command)

import copy
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from trajectory import predict
import trajectory
from test_revised import training_fixture, trace_fixture


def cohort(root):
    historical, write_old = training_fixture(root/"training")
    for row in historical["cases"]:
        if row["split"] == "validation":
            write_old(row["name"]+"/report.json", trace_fixture(row["mode"]))
            write_old(row["name"]+"/process.json", dict(exitCode=0, processGroupStopped=True, deadlineExceeded=False, elapsedSeconds=1))
    def write(name, value):
        path=root/name;path.parent.mkdir(parents=True, exist_ok=True);path.write_text(json.dumps(value))
    for name in ("image.json", "runtime-identity.json"):
        shutil.copyfile(root/"training"/name,root/name)
    write("source-identity.json", {"sampler": "new-r3-source"})
    files={p.relative_to(root/"training").as_posix():hashlib.sha256(p.read_bytes()).hexdigest()
           for p in (root/"training").rglob("*.json")}
    write("training-provenance.json", dict(archiveSha256="fixture-archive", files=files))
    contract={k:historical[k] for k in ("slots", "workers", "resetConcurrency", "cycles")}
    contract.update(trainingArchiveSha256="fixture-archive",cases=[r|dict(name="training/"+r["name"],
        split="calibration",historicalSplit=r["split"]) for r in historical["cases"]])
    for pair in (5,6):
        for mode in ("awaited", "background"):
            contract["cases"].append(dict(name=f"r{pair}-w0-{mode}",pair=pair,mode=mode,waitMilliseconds=0,split="validation"))
    write("contract.json",contract)
    return contract,write


class WholePathContract(unittest.TestCase):
    def test_complete_paths_keep_worker_order_alignment_and_initial_offsets(self):
        footer = dict(checkout_us=0, handoff_us=0, receipt_us=0, publication_us=0,
                      release_us=0, completion_us=0, dispatch_us=0)
        runs = []
        for name, scale in (("path-a", 1), ("path-b", 2)):
            streams = [[(1, 10), (3, 7)], [(5, 1), (4, 2)]]
            workers = [[footer | dict(body_us=body*1000*scale, reset_us=reset*1000*scale)
                        for body, reset in stream] for stream in streams]
            runs.append(dict(name=name, workers=workers, initial_us=[0, 2000*scale]))
        fitted = dict(settings=dict(slots=2, workers=2, resetConcurrency=1, cycles=4),
                      groups={"awaited/0": runs, "background/0": runs})
        for mode, expected in (("background", {24, 48}), ("awaited", {23, 46})):
            rows = predict(fitted, mode, 0)
            self.assertEqual(64, len(rows))
            self.assertEqual(rows, predict(fitted, mode, 0))
            self.assertEqual(expected, {r["suite_ms"] for r in rows})
            self.assertEqual({"path-a", "path-b"}, {r["calibrationRun"] for r in rows})
            a = next(r for r in rows if r["calibrationRun"] == "path-a")
            if mode == "background":
                self.assertEqual((2.75, 9, 2.25, 7, .5),
                    (a["checkout_mean_ms"], a["checkout_p95_ms"], a["reset_queue_mean_ms"],
                     a["reset_queue_p95_ms"], a["clean_starvation"]))
            else:
                self.assertEqual((0, 2.25, 5, 0), (a["checkout_mean_ms"], a["reset_queue_mean_ms"],
                    a["reset_queue_p95_ms"], a["clean_starvation"]))


class TrainingRoleContract(unittest.TestCase):
    def test_frozen_forecast_reports_five_paths_and_is_not_refitted_to_future_runs(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            fitted=trajectory.fit(root,contract);frozen=trajectory.forecast(fitted)
            self.assertEqual({"awaited/0":5,"background/0":5},frozen["trainingPathCounts"])
            self.assertEqual(64,frozen["replicates"])
            self.assertEqual(6,len(frozen["sensitivities"]))
            self.assertIn("independent-worker-records",frozen["sensitivities"])
            with self.assertRaises(OSError):trajectory.validate(root,contract,fitted,frozen)
            for row in contract["cases"]:
                if row["split"]!="validation":continue
                report=trace_fixture(row["mode"])
                for event in report["Boundaries"]:event["Tick"]*=10
                for sample in report["Samples"]:
                    for key in ("Started","Acquired","Returning","Returned"):sample[key]*=10
                for key in ("SuiteMilliseconds","FinalResetsFinishedMilliseconds","LastTestFinishedMilliseconds"):report[key]*=10
                write(row["name"]+"/report.json",report)
                write(row["name"]+"/process.json",dict(exitCode=0,processGroupStopped=True,deadlineExceeded=False,elapsedSeconds=1))
            result=trajectory.validate(root,contract,fitted,frozen)
            self.assertFalse(result["gate"]["qualified"])
            self.assertEqual(4,len(result["gate"]["perRunSuite"]))
            self.assertTrue(all(r["predicted"]["suite_ms"]<100 for r in result["runs"]))
            altered=copy.deepcopy(fitted);altered["groups"]["awaited/0"][0]["workers"][0][0]["body_us"]+=1
            with self.assertRaisesRegex(ValueError,"FitIdentity"):trajectory.validate(root,contract,altered,frozen)
            altered=copy.deepcopy(frozen);altered["predictions"]["awaited/0"]["suite_ms"]=550
            with self.assertRaisesRegex(ValueError,"ForecastIdentity"):trajectory.validate(root,contract,fitted,altered)

    def test_all_observed_runs_train_without_rewriting_their_history_or_reading_new_validation(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);contract,write=cohort(root)
            before=(root/"training/contract.json").read_bytes()
            fitted=trajectory.fit(root,contract)
            self.assertEqual(10,len(fitted["calibrationRuns"]))
            self.assertEqual(5,len(fitted["groups"]["awaited/0"]))
            self.assertEqual(5,len(fitted["groups"]["background/0"]))
            self.assertEqual(1000,fitted["groups"]["background/0"][0]["workers"][0][0]["body_us"])
            for row in contract["cases"]:
                if row["split"]=="validation":write(row["name"]+"/report.json",{"mustNotBeRead":True})
            self.assertEqual(fitted,trajectory.fit(root,contract))
            self.assertEqual(before,(root/"training/contract.json").read_bytes())
            changed=copy.deepcopy(contract);changed["cases"][0]["historicalSplit"]="validation"
            write("contract.json",changed)
            with self.assertRaisesRegex(ValueError,"TrainingProjection"):
                trajectory.fit(root,changed)

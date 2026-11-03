import copy
import json
from pathlib import Path
import tempfile
import unittest

from revised import read_trace


def trace_fixture(mode="background"):
    events, samples = [], []
    def event(stage, at, ordinal, *, slot="slot", reset=None, database=None, oid=None):
        events.append(dict(Stage=stage, Tick=round(at * 1000), Request=f"measured/{ordinal}",
                           Slot=slot, Reset=reset, Database=database, Oid=oid))
    for i in range(2):
        if mode == "background":
            requested, acquired, returning, queued, started, executed, published, released, completed, returned = (
                (0, 0, 1, 3, 3, 13, 16, 20, 22, 6) if i == 0 else
                (7, 16, 17, 19, 20, 30, 33, 37, 39, 22))
        else:
            requested, acquired, returning, queued, started, executed, published, released, completed, returned = (
                (0, 0, 1, 3, 3, 13, 16, 20, 22, 27) if i == 0 else
                (28, 28, 29, 31, 31, 41, 44, 48, 50, 55))
        samples.append(dict(Ordinal=i, Worker=0, Database="db", Oid=11, Started=requested,
                            Acquired=acquired, Returning=returning, Returned=returned))
        event("request", requested, i, slot=None)
        if requested < acquired:
            event("queued", requested, i, slot=None)
        event("admit", acquired, i)
        event("binding", acquired, i, database="db", oid=11)
        event("dirty", returning + 1, i)
        for stage, at in [("reset-request", queued), ("reset-start", started), ("service-end", executed),
                          ("publish", published), ("release-before", released),
                          ("release-after", released + .001), ("pool-settled", released + .002)]:
            event(stage, at, i, reset=f"reset-{i}")
        event("complete", completed, i)
        if mode == "background":
            event("background-complete", completed, i, slot=None)
    settled = dict(Ready=1, Reserved=1, Leased=0, WaitingResets=0, ExecutingResets=0,
                   ExecutingCleanup=0, QueuedAcquisitions=0, Preparing=0, Quarantined=0,
                   Retiring=0, Parked=0, Parking=0, Rewarming=0)
    return dict(Mode=mode, AddedWaitMilliseconds=0, Frequency=1_000_000, OriginTick=0, Samples=samples, Boundaries=events,
                KnownDatabases={"db": 11}, Succeeded=True, CleanupCompleted=True, IsolationChecked=True,
                PreflightState=settled, FinalState=settled, SuiteStartedMilliseconds=0,
                SuiteMilliseconds=39 if mode == "background" else 55,
                FinalResetsFinishedMilliseconds=39 if mode == "background" else 55,
                LastTestFinishedMilliseconds=22 if mode == "background" else 55,
                StartupMilliseconds=21, PreflightMilliseconds=32, FinalAuditMilliseconds=43,
                DisposalMilliseconds=54, TotalMilliseconds=700)


class JointTraceContract(unittest.TestCase):
    settings = dict(slots=1, workers=1, resetConcurrency=1, cycles=2)

    def test_explicit_identity_joins_separate_services_from_generated_queues(self):
        for mode in ("awaited", "background"):
            report = trace_fixture(mode)
            # Emission order is not time order around semaphore continuations.
            report["Boundaries"].reverse()
            result = read_trace(report, self.settings)
            self.assertEqual([0], result["initial_us"])
            first, second = result["workers"][0]
            expected = dict(body_us=1000, reset_us=10000, checkout_us=0, handoff_us=2000,
                            receipt_us=5000, publication_us=3000, release_us=4000,
                            completion_us=2000, dispatch_us=0)
            self.assertEqual(expected, first)
            self.assertEqual(expected | dict(dispatch_us=1000), second)
            observed = result["observed"]
            self.assertEqual(39 if mode == "background" else 55, observed["suite_ms"])
            self.assertEqual(.5 if mode == "background" else 0, observed["clean_starvation"])
            self.assertEqual(4.5 if mode == "background" else 0, observed["clean_queue_mean_ms"])
            self.assertEqual(.5 if mode == "background" else 0, observed["reset_queue_mean_ms"])
            self.assertEqual([15000, 15001] if mode == "background" else [0, 0], result["drain"]["physicalDrainBracket_us"])
            self.assertEqual(17000 if mode == "background" else 0, result["drain"]["wrapperDrain_us"])
            self.assertEqual(0, result["drain"]["settlementTail_us"])

    def test_missing_duplicate_mismatched_or_unsafe_events_cannot_supply_services(self):
        for fault, diagnostic in [("missing", "TraceEvents"), ("duplicate", "TraceEvents"),
                                  ("identity", "TraceIdentity"), ("dirty", "TraceOwnership"),
                                  ("timing", "TraceTiming"), ("permits", "TracePermits"),
                                  ("drain", "TraceSettlement"), ("reset_alias", "TraceIdentity")]:
            report = trace_fixture()
            if fault == "missing":
                report["Boundaries"] = [e for e in report["Boundaries"] if e["Stage"] != "release-after"]
            elif fault == "duplicate":
                report["Boundaries"].append(copy.deepcopy(report["Boundaries"][0]))
            elif fault == "identity":
                next(e for e in report["Boundaries"] if e["Stage"] == "binding")["Database"] = "other"
            elif fault == "drain":
                report["FinalState"]["Ready"] = 0
            elif fault == "reset_alias":
                for event in report["Boundaries"]:
                    if event["Reset"] == "reset-1":
                        event["Reset"] = "reset-0"
            else:
                stage = {"dirty": "admit", "timing": "service-end", "permits": "reset-start"}[fault]
                target = next(e for e in report["Boundaries"] if e["Stage"] == stage and e["Request"] == "measured/1")
                target["Tick"] = {"dirty": 15000, "timing": 18000, "permits": 19000}[fault]
            with self.subTest(fault=fault), self.assertRaisesRegex(ValueError, diagnostic):
                read_trace(report, self.settings)


class JointPredictionContract(unittest.TestCase):
    def test_qualification_also_requires_actual_clean_queue_and_starvation_metrics(self):
        import revised
        baseline = dict(suite_ms=100, checkout_mean_ms=1, checkout_p95_ms=2,
                        reset_queue_mean_ms=1, reset_queue_p95_ms=2, starvation_proxy=.2,
                        clean_starvation=.2, clean_queue_mean_ms=1)
        rows = [dict(name="held-out", mode="background", waitMilliseconds=0,
                     observed=baseline, predicted=baseline.copy())]
        self.assertTrue(revised.assess(rows)["qualified"])
        for key, bad in [("clean_starvation", .301), ("clean_queue_mean_ms", 4.01), ("suite_ms", 111)]:
            changed = copy.deepcopy(rows)
            changed[0]["predicted"][key] = bad
            with self.subTest(key=key):
                result = revised.assess(changed)
                self.assertFalse(result["qualified"])
                self.assertFalse(result["strata"][0]["metrics"][key]["passed"])
        rows[0]["predicted"]["clean_queue_mean_ms"] = float("nan")
        with self.assertRaisesRegex(ValueError, "PredictionMetrics"):
            revised.assess(rows)

    def test_one_run_supplies_every_joint_service_without_impossible_hybrids(self):
        import revised
        settings = dict(slots=1, workers=1, resetConcurrency=1, cycles=2)
        base = dict(checkout_us=0, handoff_us=0, receipt_us=0, publication_us=0,
                    release_us=0, completion_us=0, dispatch_us=0)
        runs = [dict(name=name, initial_us=[0], workers=[[base | dict(body_us=body, reset_us=reset)]])
                for name, body, reset in [("slow-body", 10000, 1000), ("slow-reset", 1000, 10000)]]
        fitted = dict(settings=settings, groups={"awaited/0": runs, "background/0": runs})
        for mode in ("awaited", "background"):
            draws = revised.predict(fitted, mode, 0)
            self.assertEqual(64, len(draws))
            self.assertEqual(draws, revised.predict(fitted, mode, 0))
            self.assertEqual({"slow-body", "slow-reset"}, {r["calibrationRun"] for r in draws})
            # Every paired record totals 11 ms. Independent component/run draws
            # could invent 2 or 20 ms tests that were never observed.
            self.assertEqual({22}, {r["suite_ms"] for r in draws})
            self.assertTrue(all(r["clean_starvation"] == (0 if mode == "awaited" else .5) for r in draws))
            self.assertTrue(all(r["suite_ms"] > 22 for r in revised.predict(fitted, mode, 0, reset_scale=1.2)))


def training_fixture(root):
    contract = dict(slots=1, workers=1, resetConcurrency=1, cycles=2, cases=[])
    def write(name, value):
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))
    for pair in range(5):
        for mode in ("awaited", "background"):
            row = dict(name=f"r{pair}-w0-{mode}", pair=pair, mode=mode, waitMilliseconds=0,
                       split="calibration" if pair < 3 else "validation")
            contract["cases"].append(row)
            if pair < 3:
                write(row["name"] + "/report.json", trace_fixture(mode))
                write(row["name"] + "/process.json", dict(exitCode=0, processGroupStopped=True, deadlineExceeded=False, elapsedSeconds=1))
    for name in ("source-identity.json", "runtime-identity.json", "image.json"):
        write(name, {"recorded": name})
    write("contract.json", contract)
    return contract, write


class ProspectiveFitContract(unittest.TestCase):
    def test_frozen_forecasts_precede_validation_and_reject_edited_fit_or_predictions(self):
        import revised
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            contract, write = training_fixture(root)
            fitted = revised.fit(root, contract)
            forecast = revised.forecast(fitted)
            self.assertEqual((64, 2, 6), (forecast["replicates"], len(forecast["predictions"]), len(forecast["sensitivities"])))
            with self.assertRaises(OSError):
                revised.validate(root, contract, fitted, forecast)
            for row in contract["cases"]:
                if row["split"] != "validation":
                    continue
                report = trace_fixture(row["mode"])
                for event in report["Boundaries"]:
                    event["Tick"] *= 10
                for sample in report["Samples"]:
                    for key in ("Started", "Acquired", "Returning", "Returned"):
                        sample[key] *= 10
                for key in ("SuiteMilliseconds", "FinalResetsFinishedMilliseconds", "LastTestFinishedMilliseconds"):
                    report[key] *= 10
                write(row["name"] + "/report.json", report)
                write(row["name"] + "/process.json", dict(exitCode=0, processGroupStopped=True, deadlineExceeded=False, elapsedSeconds=1))
            result = revised.validate(root, contract, fitted, forecast)
            self.assertFalse(result["gate"]["qualified"])
            self.assertEqual(4, len(result["gate"]["perRunSuite"]))
            self.assertTrue(all(row["predicted"]["suite_ms"] < 100 for row in result["runs"]))
            self.assertTrue(all(not row["passed"] for row in result["gate"]["perRunSuite"]))
            changed = copy.deepcopy(fitted)
            changed["groups"]["awaited/0"][0]["workers"][0][0]["body_us"] += 1
            with self.assertRaisesRegex(ValueError, "FitIdentity"):
                revised.validate(root, contract, changed, forecast)
            changed = copy.deepcopy(forecast)
            changed["predictions"]["awaited/0"]["suite_ms"] = 550
            with self.assertRaisesRegex(ValueError, "ForecastIdentity"):
                revised.validate(root, contract, fitted, changed)

    def test_fitting_needs_only_calibration_and_binds_the_frozen_split_and_sources(self):
        import revised
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            contract, write = training_fixture(root)
            first = revised.fit(root, contract)
            self.assertEqual(6, len(first["calibrationRuns"]))
            self.assertEqual(3, len(first["groups"]["background/0"]))
            self.assertEqual(10000, first["groups"]["background/0"][0]["workers"][0][0]["reset_us"])
            for row in contract["cases"]:
                if row["split"] == "validation":
                    write(row["name"] + "/report.json", {"mustNotBeReadForFitting": True})
            self.assertEqual(first, revised.fit(root, contract))
            write("r0-w0-awaited/report.json", trace_fixture("awaited") | dict(AddedWaitMilliseconds=25))
            with self.assertRaisesRegex(ValueError, "TraceIdentity"):
                revised.fit(root, contract)
            changed = copy.deepcopy(contract)
            changed["cases"][-1]["split"] = "calibration"
            write("contract.json", changed)
            with self.assertRaisesRegex(ValueError, "ContractSplit"):
                revised.fit(root, changed)

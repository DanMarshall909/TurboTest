import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from reference import assess, evaluate, fit, predict, verify_inputs


class TraceFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.pins = dict(files={}, runs=[], contractSha256=None)
        cases = []
        for pair in range(5):
            for mode in ["awaited", "background"]:
                name = f"r{pair}-w0-{mode}"
                case = dict(name=name, pair=pair, mode=mode, waitMilliseconds=0)
                cases.append(case)
                self.pins["runs"].append(case | dict(split="calibration" if pair < 3 else "validation"))
                samples = [dict(Ordinal=i, Worker=i % 2, Started=i * 100,
                                Acquired=i * 100 + 2, Returning=i * 100 + 5 + i,
                                Returned=i * 100 + (90 if mode == "awaited" else 6 + i))
                           for i in range(4)]
                resets = [dict(ExecutionMilliseconds=i + 7, PermitWaitMilliseconds=0,
                               ValidationMilliseconds=1) for i in range(4)]
                self.write(name + "/report.json", dict(Samples=samples, Resets=[{}] * 2 + resets,
                           Warmups=0, SuiteMilliseconds=500, FinalResetWaitMilliseconds=3,
                           StartupMilliseconds=21, PreflightMilliseconds=32, FinalAuditMilliseconds=43,
                           DisposalMilliseconds=54, TotalMilliseconds=700))
                self.write(name + "/process.json", dict(exitCode=0, elapsedSeconds=.8))
        self.write("source-identity.json", {"source": "recorded-sha"})
        self.write("runtime-identity.json", {"runtime": "recorded-sha"})
        self.write("contract.json", dict(cases=cases, slots=2, workers=2,
                                        resetConcurrency=1, cycles=4))
        self.pins["contractSha256"] = self.pins["files"]["contract.json"]
        self.write("contract-binding.json", dict(sha256=self.pins["contractSha256"]))

    def write(self, name, value):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))
        self.pins["files"][name] = hashlib.sha256(path.read_bytes()).hexdigest()


class TraceContract(TraceFixture):
    def test_pin_verification_rejects_changed_missing_or_unpinned_inputs(self):
        self.assertEqual(4, verify_inputs(self.root, self.pins)["cycles"])
        name = self.pins["runs"][0]["name"] + "/report.json"
        original = (self.root / name).read_bytes()
        (self.root / name).write_text("changed")
        with self.assertRaisesRegex(ValueError, "InputHash:.*" + name):
            verify_inputs(self.root, self.pins)
        (self.root / name).unlink()
        with self.assertRaisesRegex(ValueError, "InputMissing:.*" + name):
            verify_inputs(self.root, self.pins)
        (self.root / name).write_bytes(original)
        del self.pins["files"][name]
        with self.assertRaisesRegex(ValueError, "InputPins"):
            verify_inputs(self.root, self.pins)

    def test_whole_run_partition_cannot_be_changed_duplicated_or_truncated(self):
        for change in ["duplicate", "omit", "leak", "mode"]:
            pins = copy.deepcopy(self.pins)
            if change == "duplicate":
                pins["runs"].append(pins["runs"][0])
            elif change == "omit":
                pins["runs"].pop()
            elif change == "leak":
                pins["runs"][-1]["split"] = "calibration"
            else:
                pins["runs"][0]["mode"] = "background"
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, "RunSplit"):
                verify_inputs(self.root, pins)

    def test_fit_retains_service_variability_and_ignores_validation_timings(self):
        first = fit(self.root, self.pins)
        for row in self.pins["runs"]:
            if row["split"] == "validation":
                self.write(row["name"] + "/report.json", {"unreadableAsServiceSamples": True})
        second = fit(self.root, self.pins)
        self.assertEqual(first["groups"], second["groups"])
        group = first["groups"]["awaited/0"]
        self.assertEqual(3, len(group))
        self.assertEqual([[3000, 5000], [4000, 6000]], group[0]["body_us"])
        self.assertEqual([7000, 8000, 9000, 10000], group[0]["reset_us"])
        # Awaited return includes its reset; no 85 ms duration may enter handoff.
        self.assertEqual([1000] * 4, first["groups"]["background/0"][0]["handoff_us"])
        self.assertNotIn("handoff_us", group[0])
        self.assertEqual([2000, 2000], group[0]["checkout_us"])

    def test_invalid_trace_timing_ordinals_resets_and_binding_are_rejected(self):
        name = self.pins["runs"][0]["name"] + "/report.json"
        original = json.loads((self.root / name).read_text())
        for change, diagnostic in [("time", "TraceTiming"), ("ordinal", "TraceSamples"),
                                   ("reset", "TraceResets")]:
            report = copy.deepcopy(original)
            if change == "time":
                report["Samples"][0]["Returning"] = -1
            elif change == "ordinal":
                report["Samples"][0]["Ordinal"] = 99
            else:
                report["Resets"].pop()
            self.write(name, report)
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, diagnostic):
                fit(self.root, self.pins)
        self.write(name, original)
        self.write("contract-binding.json", dict(sha256="wrong"))
        with self.assertRaisesRegex(ValueError, "InputPins: contract binding"):
            verify_inputs(self.root, self.pins)

    def test_evaluation_reports_all_runs_but_qualifies_only_the_held_out_partition(self):
        result = evaluate(self.root, self.pins, fit(self.root, self.pins))
        self.assertEqual(10, len(result["runs"]))
        self.assertEqual(4, len(result["gate"]["perRunSuite"]))
        self.assertTrue(all(r["name"].startswith(("r3-", "r4-")) for r in result["gate"]["perRunSuite"]))
        self.assertFalse(result["gate"]["qualified"])
        self.assertEqual(6, len(result["sensitivities"]))
        row = result["runs"][0]
        self.assertEqual(2, row["observed"]["checkout_mean_ms"])
        self.assertEqual(500, row["observed"]["suite_ms"])
        self.assertEqual(3, row["observed"]["drain_ms"])
        self.assertEqual(800, row["unmodelledObservedCosts"]["processWallMilliseconds"])
        self.assertEqual(21, row["unmodelledObservedCosts"]["StartupMilliseconds"])
        for mode in ["awaited", "background"]:
            self.assertEqual(set(row["predicted"]), set(result["predictionBounds"][mode + "/0"]))

    def test_saved_fit_must_match_pinned_training_inputs_and_protocol(self):
        fitted = fit(self.root, self.pins)
        for change in ["pins", "protocol", "samples"]:
            changed = copy.deepcopy(fitted)
            if change == "pins":
                changed["inputPinsSha256"] = "wrong"
            elif change == "protocol":
                changed["protocolSha256"] = "wrong"
            else:
                changed["groups"]["awaited/0"][0]["body_us"][0][0] = 999
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, "FitIdentity"):
                evaluate(self.root, self.pins, changed)


class PredictionContract(unittest.TestCase):
    def fitted(self):
        service = dict(body_us=[[1000, 1000], [1000, 1000]], reset_us=[10000] * 4,
                       checkout_us=[0, 0], handoff_us=[0] * 4)
        return dict(settings=dict(slots=2, workers=2, resetConcurrency=1, cycles=4),
                    groups={"background/0": [service], "awaited/0": [service]})

    def test_seeded_prediction_preserves_queue_and_drain_endpoints(self):
        fitted = self.fitted()
        result = predict(fitted, "background", 0)
        self.assertEqual(64, len(result))
        self.assertEqual(result, predict(fitted, "background", 0))
        self.assertTrue(all(row["suite_ms"] == 41 for row in result))
        self.assertEqual(dict(checkout_mean_ms=7.5, checkout_p95_ms=20,
                              reset_queue_mean_ms=7, reset_queue_p95_ms=10,
                              starvation_proxy=.5, drain_ms=19, virtual_queue_mean_ms=7.5),
                         {k: v for k, v in result[0].items() if k != "suite_ms"})
        self.assertGreater(predict(fitted, "background", 0, reset_scale=1.2)[0]["suite_ms"], 41)
        self.assertLess(predict(fitted, "background", 0, body_scale=.8)[0]["suite_ms"], 41)
        self.assertGreater(predict(fitted, "background", 0, tail_us=2000)[0]["suite_ms"], 41)

    def test_qualification_requires_all_strata_metrics_and_per_run_suite_limits(self):
        baseline = dict(suite_ms=100, checkout_mean_ms=10, checkout_p95_ms=20,
                        reset_queue_mean_ms=1, reset_queue_p95_ms=2, starvation_proxy=.5,
                        drain_ms=0)
        rows = [dict(name=f"run-{i}", mode="background", waitMilliseconds=0,
                     observed=baseline.copy(), predicted=baseline.copy()) for i in range(2)]
        self.assertTrue(assess(rows)["qualified"])
        for metric, bad in [("suite_ms", 111), ("checkout_mean_ms", 13.1),
                            ("checkout_p95_ms", 30.1), ("reset_queue_mean_ms", 4.1),
                            ("reset_queue_p95_ms", 12.1), ("starvation_proxy", .601)]:
            changed = copy.deepcopy(rows)
            for row in changed:
                row["predicted"][metric] = bad
            with self.subTest(metric=metric):
                result = assess(changed)
                self.assertFalse(result["qualified"])
                self.assertFalse(result["strata"][0]["metrics"][metric]["passed"])
        # A good stratum median must not hide one suite run missing its 20% bound.
        changed = copy.deepcopy(rows)
        changed[0]["predicted"]["suite_ms"] = 121
        changed[1]["predicted"]["suite_ms"] = 79
        self.assertFalse(assess(changed)["qualified"])
        changed = copy.deepcopy(rows)
        for row in changed:
            row["predicted"]["checkout_mean_ms"] = 13
        self.assertTrue(assess(changed)["qualified"])

    def test_gate_cannot_qualify_empty_or_nonfinite_predictions(self):
        for rows in [[], [dict(name="bad", mode="background", waitMilliseconds=0,
                              observed=dict(suite_ms=float("nan")), predicted=dict(suite_ms=1))]]:
            with self.subTest(rows=rows), self.assertRaisesRegex(ValueError, "PredictionMetrics"):
                assess(rows)


if __name__ == "__main__":
    unittest.main()

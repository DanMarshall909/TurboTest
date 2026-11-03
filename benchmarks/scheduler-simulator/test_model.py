import unittest

from model import Work, simulate


class PipelineContract(unittest.TestCase):
    def test_receipt_overlap_publication_release_and_wrapper_completion_are_distinct(self):
        jobs = [Work(1, 10, handoff_us=2, receipt_us=5, publication_us=3,
                     release_us=4, completion_us=2, dispatch_us=offset) for offset in (0, 1)]
        background = simulate(jobs, mode="background", slots=1, workers=1, reset_workers=1)
        self.assertEqual((39, 22, 17), (background["suite_us"], background["last_return_us"], background["drain_us"]))
        self.assertEqual([0, 7], [t["requested"] for t in background["tests"]])
        self.assertEqual([0, 16], [t["acquired"] for t in background["tests"]])
        self.assertEqual([3, 20], [r["started"] for r in background["resets"]])
        self.assertEqual([13, 30], [r["executed"] for r in background["resets"]])
        self.assertEqual([16, 33], [r["published"] for r in background["resets"]])
        self.assertEqual([20, 37], [r["released"] for r in background["resets"]])
        self.assertEqual([22, 39], [r["completed"] for r in background["resets"]])
        awaited = simulate(jobs, mode="awaited", slots=1, workers=1, reset_workers=1)
        self.assertEqual((55, 55, 0), (awaited["suite_us"], awaited["last_return_us"], awaited["drain_us"]))
        self.assertEqual([0, 28], [t["requested"] for t in awaited["tests"]])

    def test_awaited_return_joins_reset_and_background_drain_joins_it_later(self):
        for mode, returned, drain in [("awaited", 40, 0), ("background", 15, 25)]:
            with self.subTest(mode=mode):
                result = simulate([Work(10, 20, 2, 3)], mode=mode, slots=1,
                                  workers=1, reset_workers=1, release_tail_us=5)
                self.assertEqual(40, result["suite_us"])
                self.assertEqual(returned, result["tests"][0]["returned"])
                self.assertEqual(drain, result["drain_us"])
                self.assertEqual((2, 12), (result["tests"][0]["acquired"],
                                          result["tests"][0]["returning"]))

    def test_dirty_slots_wait_for_fifo_resets_before_admitting_oldest_checkout(self):
        result = simulate([Work(1, 10)] * 4, mode="background", slots=2,
                          workers=2, reset_workers=1)
        self.assertEqual([0, 0, 11, 21], [t["acquired"] for t in result["tests"]])
        self.assertEqual([0, 0, 10, 20], [t["queue_us"] for t in result["tests"]])
        self.assertEqual([1, 11, 21, 31], [r["started"] for r in result["resets"]])
        self.assertEqual([0, 1, 2, 3], [r["ordinal"] for r in result["resets"]])
        self.assertEqual((41, 19, 1), (result["suite_us"], result["drain_us"],
                                       result["peak_resets"]))

    def test_publication_can_serve_next_test_while_prior_reset_still_holds_permit(self):
        result = simulate([Work(1, 10)] * 2, mode="background", slots=1,
                          workers=1, reset_workers=1, release_tail_us=5)
        self.assertEqual(11, result["tests"][1]["acquired"])
        self.assertEqual(16, result["resets"][1]["started"])
        self.assertEqual(31, result["suite_us"])
        self.assertEqual((1, 0), (result["final_clean"], result["final_reset_permits"]))

    def test_service_variability_worker_order_and_exclusive_ownership_are_repeatable(self):
        jobs = [Work(2 + i % 7, 11 - i % 5, i % 3, 1) for i in range(37)]
        for mode in ["awaited", "background"]:
            for slots, workers, resets in [(1, 3, 2), (4, 3, 1), (4, 3, 4)]:
                with self.subTest(mode=mode, slots=slots, resets=resets):
                    kwargs = dict(mode=mode, slots=slots, workers=workers,
                                  reset_workers=resets, release_tail_us=3)
                    result = simulate(jobs, **kwargs)
                    self.assertEqual(result, simulate(jobs, **kwargs))
                    self.assertEqual(list(range(37)), [t["ordinal"] for t in result["tests"]])
                    self.assertEqual(37, len(result["resets"]))
                    self.assertLessEqual(result["peak_resets"], resets)
                    self.assertEqual((slots, 0), (result["final_clean"], result["final_reset_permits"]))
                    by_reset = {r["ordinal"]: r for r in result["resets"]}
                    for t in result["tests"]:
                        self.assertEqual(t["ordinal"] % workers, t["worker"])
                        self.assertEqual(jobs[t["ordinal"]].body_us, t["returning"] - t["acquired"])
                        prior = [r for r in result["resets"] if r["slot"] == t["slot"]
                                 and r["ordinal"] != t["ordinal"] and r["started"] < t["acquired"]]
                        self.assertTrue(all(r["published"] <= t["acquired"] for r in prior))
                        if t["ordinal"] >= workers:
                            self.assertEqual(result["tests"][t["ordinal"] - workers]["returned"], t["requested"])
                        self.assertEqual(jobs[t["ordinal"]].reset_us,
                                         by_reset[t["ordinal"]]["published"] - by_reset[t["ordinal"]]["started"])

    def test_zero_duration_jobs_still_conserve_slots_and_finish_every_reset(self):
        result = simulate([Work(0, 0)] * 7, mode="background", slots=2,
                          workers=3, reset_workers=1)
        self.assertEqual((0, 7, 7, 2, 0), (result["suite_us"], len(result["tests"]),
                         len(result["resets"]), result["final_clean"], result["final_reset_permits"]))

    def test_invalid_capacity_mode_or_nonfinite_timing_is_rejected(self):
        valid = dict(mode="background", slots=2, workers=2, reset_workers=1)
        for patch in [dict(slots=0), dict(workers=-1), dict(reset_workers=0),
                      dict(mode="other"), dict(release_tail_us=-1), dict(slots=1.5)]:
            with self.subTest(patch=patch), self.assertRaisesRegex(ValueError, "ModelInput"):
                simulate([Work(1, 1)], **(valid | patch))
        for job in [Work(-1, 1), Work(1, float("inf")), Work(1, 1, float("nan"))]:
            with self.subTest(job=job), self.assertRaisesRegex(ValueError, "ModelInput"):
                simulate([job], **valid)
        with self.assertRaisesRegex(ValueError, "ModelInput"):
            simulate([], **valid)


if __name__ == "__main__":
    unittest.main()

# Complete performance rerun: preparation and execution boundary

Live work: [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).
Dan requested TT-038 plus every TT-039 performance check again, with all other
work completed first. **No performance run is authorized yet.** Do not create
`artifacts/tt039-r4/measurement-window.json` until Dan explicitly confirms the
prepared source checkpoint and measurement window.

Earlier measurements and failed model attempts stay historical. The new fit
uses only new calibration data; old timings cannot establish current qualification.

## Required work before asking to measure

- Finish code, fixed manifests, functional tests, archive regression checks,
  source-bound coverage and independent readiness review.
- Prepare metadata/source snapshots and verify retained runtimes without running
  native experiments. TT-038 uses its v4 runtimes: 96 files per mode, differing
  only in `TurboTest.EFCore.dll`. TT-039 uses the pinned 24-file instrumented runtime.
- Freeze protocol, all case identities/order, code and runtime/image identities
  in a Git checkpoint. Record `protocol-r4-binding.json` and the corresponding
  local `protocol-freeze.json` before any timing control or calibration.
- Verify storage, stopped task-owned workers and the exact reviewed source.
  Preserve other sessions, containers, caches and historical evidence.
- Present Dan with the complete run set, budget and source checkpoint, then ask
  for confirmation. Scope agreement alone is not permission to measure.

## Fixed run set after confirmation

| Phase | Count / workload | Guard and supervision |
| --- | --- | --- |
| TT-038 qualification | Two 16-cycle/8-warmup smokes; two 8-cycle/4-warmup expected distribution failures | `baseline_rerun.py qualify`; receipt `tt038-baseline/v4/qualification.json` |
| TT-038 baseline comparison | 40 cases, five pairs per 0/25/100/500 ms wait, 256 measured tests and 32 warmups each | `baseline_rerun.py measure`; unchanged v4 schedule; fresh root |
| TT-039 qualification | Both modes: 16/8 smoke, 8/4 distribution failure and held-reset control | `collect_r4.py qualify`; receipt `qualification.json` |
| TT-039 probe cost | Enabled/disabled, three rounds, 50,000 calls | Included in qualification; native `probe-cost.json` and distinct supervisor receipt |
| TT-039 instrumentation cost | Three counterbalanced on/off pairs, six background cases, 64 measured tests/32 warmups | Included in qualification; retain all six outcomes |
| Fresh TT-039 calibration | Seven complete paired rounds, 56 cases, 256/32 each | `collect_r4.py calibrate`; source and collection-ready records committed first |
| Fit and freeze | Exact whole-path expectation and all six sensitivities | `replicated.py fit`, then `forecast`; commit all calibration/fit/forecast bindings before validation |
| Fresh TT-039 validation | 84 cases: 21 /25 visits per mode, seven per other stratum, 256/32 each | `collect_r4.py validate`; fixed schedule, no result-driven stopping |
| Analysis | Fresh baseline comparison and both reference gates | `baseline_rerun.py analyze`; `replicated.py validate`; retain every signed error |

Primary cohorts total **180 cases / 46,080 measured tests / 5,760 warmups**,
excluding controls. TT-038 compares two historical production libraries;
TT-039 compares awaited/background behavior in one traced executable. Keep
these claims and results separate.

Use the existing `scripts.verify.run_phase` supervisor for every phase. Native
cases have 300-second deadlines. Reserve 480 seconds before admitting another
case; primary collection phases have a two-hour cap and a longer outer observer
deadline. Controls have their own finite supervision. Suggested whole-window
planning allowance is up to three hours; actual permission and expiry come from
Dan's confirmation. Historical runtimes are planning estimates only.

## Confirmation record and stopping

The local measurement record contains `approvedBy: Dan`, an actual
`approvalReference`, the exact `sourceCheckpoint`, scopes `TT-038` and `TT-039`,
and `validUntilUtc`. It is a record of confirmation, not automated proof that
a human approved. Never fabricate it from a test fixture, silence or elapsed time.
All entrypoints and per-case admission recheck it. `STOP_PERFORMANCE` withdraws
admission. Missing/stale/withdrawn permission, correctness/cleanup failure,
source drift or budget exhaustion leaves a retained incomplete attempt.

Run the prescribed cohort despite prediction misses. No added rounds, selective
reruns, dropped cases, threshold changes or model changes after validation.
Both the original first-two gate and full-cohort gate must pass, as must all
84 individual suite checks. A passing larger cohort cannot rescue the first two.

## Evidence and closeout

Retain the complete new source/runtime/image/window bindings, controls, raw
reports, process/resource receipts, calibration, forecast and evaluation.
Reproduce fresh results from an archive; keep historical archive regressions
separate. Verify owned cleanup without deleting other sessions' resources.
The runtime directories are read-only symlinks during execution. Archive the
manifest-listed target bytes explicitly, or pin the published dependency archive
and verify each required binary when restoring. A tar member containing only
the symlink is insufficient. Extract into a clean temporary root and reproduce
both baseline analysis and the full R4 fit/forecast/evaluation before closeout.
Obtain independent completion review of the exact final candidate, publish
normally under repository policy and read back the issue update. No policy
search, release, runtime controller, public metadata API or new backend follows
automatically from this rerun.

Supplementary R3 CI failed native teardown tests under SDK/runtime
10.0.403/10.0.14. All 181 native tests pass locally under 10.0.301/10.0.9;
the discrepancy remains documented. It is not a new performance result.

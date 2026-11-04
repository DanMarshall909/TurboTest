# Fresh scheduler reference qualification and TT-038 baseline rerun

Investigation for [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).

## Result

The frozen R4 whole-run reference model **does not qualify**. Both required
held-out gates fail, so this result does not authorize scheduler policy search,
runtime controller work, or a product claim.

The separate TT-038 production-library comparison passes its execution and
analysis contract. Across five counterbalanced pairs at each added wait, the
background-return library has a lower median physical-reset suite time in all
20 pairs. This comparison remains distinct from the failed TT-039 predictive
qualification.

The complete requested primary workload ran after Dan confirmed the machine was
ready: 180 cases, 46,080 measured tests and 5,760 warmups. One native producer
ran at a time. Every primary case completed before the approval window expired,
its owned process group stopped, and its resource inventory settled unchanged.

## Frozen identities and sequence

- Protocol/source checkpoint: `37c1532f4ca931c923200a0c64bae60c8dadb3a7`.
- Collection-ready checkpoint: `ca48fa8de6e243fbe934029f35f24e3d37ad087c`.
- Forecast checkpoint: `7adc82ab87ea937b2a4cd13f0db85ec39036f519`.
- Approval window: 2026-11-04 05:56:59–08:56:59 UTC, scopes TT-038 and TT-039.
- Collection order: qualification controls, fresh TT-038 comparison, fresh
  TT-039 calibration, fit and forecast freeze, then fresh TT-039 validation.
- Case deadline: 300 seconds; primary phase budget: 7,200 seconds; outer
  supervisor deadline: 7,800 seconds.

The R4 fit uses only the 56 fresh calibration cases. The 84 validation cases
were absent when the fit and forecast were created and committed. No cases were
added, dropped, selectively rerun, or used to change a threshold or model.

## Native controls

TT-038 completed two 16-cycle smokes and two expected 8-cycle distribution
faults. TT-039 completed two 16-cycle smokes, two expected 8-cycle distribution
faults, and both held-reset controls. The awaited held control retained its
receipt until reset completion; the background control did not, and both showed
a clean alternate slot, waited on exhausted acquisition, and joined reset work
during shutdown.

The six 50,000-event probe rounds reported these microseconds per event:

| Probe | Round 0 | Round 1 | Round 2 | Median |
| --- | ---: | ---: | ---: | ---: |
| disabled | 0.239532 | 0.000470 | 0.000470 | 0.000470 |
| enabled | 0.088610 | 0.091418 | 0.090512 | 0.090512 |

The counterbalanced instrumentation cases all completed and settled. Their
suite times were 1,270.670/1,267.803 ms (on/off), 1,000.362/1,284.727 ms, and
1,003.448/1,159.825 ms. They are retained as controls rather than interpreted
as an independent population estimate from three pairs on a shared host.

## TT-038 baseline comparison

The new v4 root contains 40 cases, 10,240 measured tests and 1,280 warmups.
Every case passed correctness, isolation, cleanup, fixed resource, OID, sample
ordering and resource settlement checks.

| Added wait | Awaited median suite (ms) | Background median suite (ms) | Background / awaited | Lower suite time | Wins | Exact paired bootstrap pointwise 95% interval |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 ms | 5,886.583 | 4,232.012 | 0.718925 | 28.108% | 5/5 | [0.678132, 0.752941] |
| 25 ms | 7,248.841 | 4,084.313 | 0.563444 | 43.656% | 5/5 | [0.516988, 0.662744] |
| 100 ms | 11,869.748 | 8,465.540 | 0.713203 | 28.680% | 5/5 | [0.697664, 0.795307] |
| 500 ms | 37,473.316 | 33,973.893 | 0.906616 | 9.338% | 5/5 | [0.901986, 0.914271] |

Added waits are synthetic asynchronous waits beyond the real database work.
The five pairs ran on one uncontrolled shared host, and the intervals are
pointwise with no multiplicity adjustment. Physical-reset suite time and full
process wall time remain separate endpoints.

## TT-039 fresh fit and frozen forecast

Calibration contains seven complete replay paths in each of the eight strata,
for 56 cases, 14,336 measured tests and 1,792 warmups. The forecast uses the
exact equal-weight expectation of complete replay-path metrics and retains all
six prespecified sensitivities.

| Stratum | Frozen suite prediction (ms) |
| --- | ---: |
| awaited / 0 ms | 5,932.170 |
| awaited / 25 ms | 7,187.351 |
| awaited / 100 ms | 11,934.892 |
| awaited / 500 ms | 37,725.155 |
| background / 0 ms | 4,001.243 |
| background / 25 ms | 4,539.303 |
| background / 100 ms | 8,499.437 |
| background / 500 ms | 34,112.561 |

## TT-039 held-out validation

Validation contains the complete fixed 84 cases, 21,504 measured tests and
2,688 warmups. The protocol requires the initial two visits in every stratum,
the full cohort, and every individual suite check to pass.

| Gate | Aggregate metrics | Individual suites | Result |
| --- | ---: | ---: | --- |
| Initial two visits | 63/64 pass | 16/16 pass | **FAIL** |
| Full cohort | 63/64 pass | 83/84 pass | **FAIL** |

Both aggregate gates miss the same `background / 25 ms` suite metric:

| Gate | Predicted (ms) | Observed (ms) | Signed error (ms) | Relative error | Allowance |
| --- | ---: | ---: | ---: | ---: | ---: |
| Initial two | 4,539.303 | 3,905.473 | +633.830 | +16.229% | 390.547 ms |
| Full cohort | 4,539.303 | 4,013.538 | +525.765 | +13.100% | 401.354 ms |

The full cohort also has one individual suite miss. `r25-w25-awaited` was
predicted at 7,187.351 ms and observed at 10,634.671 ms, a -3,447.320 ms signed
error (-32.416%) against a 2,126.934 ms allowance. Every other individual suite
and every other aggregate metric passed its frozen bound.

The complete cohort cannot rescue the required initial-two gate. The repeated
background 25 ms overprediction also shows that the remaining error is not only
the single awaited outlier. R4 therefore supplies useful model diagnostics but
does not establish predictive credibility for policy search.

## Reproduction and retained evidence

`raw-evidence.tar.gz` contains 2,903 files and is 40,222,300 bytes with SHA-256
`dbcee6266c1cb52a2d5fba74b7988ea6e4bd6fe1c596b11e93be867bd21a042a`.
It embeds the exact 24-file TT-039 runtime and both exact 96-file TT-038
runtimes, rather than relying on runtime symlinks. The manifest binds every
archive member, the model inputs, baseline inputs, runtime files, source,
collection and forecast checkpoints, and the earlier published TT-038 evidence
used as provenance.

The clean-room archive contract verifies every selected member hash, rebuilds
the R4 fit and forecast evaluation, reproduces the same negative gate, verifies
all three runtime identities, reruns the TT-038 analysis, and reproduces its
summary exactly. `cleanup.json` records 194 unchanged per-case settlements,
three unchanged control settlements, stopped required supervisors and no live
task-owned measurement process. The bounded storage inventory is intentionally
marked incomplete because the local evidence root retains read-only runtime
symlinks; the archive closes that portability gap with explicit runtime bytes.

## Decision

Retain R4 as a failed prospective qualification attempt. Do not search scheduler
policies against this reference. Any future attempt needs a new protocol and new
holdout; the R4 validation set is now observed development information.

# TT-039 second reference-model protocol

This is the accepted follow-up to the [first failed attempt](../../docs/issues/evidence/2026-11-03/tt039/report.md).
GitHub [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38) owns live status.
Retain the first protocol, fit, validation and failures unchanged. This protocol
must be committed before collecting the new measured runs or fitting revision 2.

## Question and scope

Can joint empirical samples from one whole calibration run predict another
successful warm FIFO run at eight databases, four test workers and four reset
permits? Both awaited and background returns use the same current production
pool, schema, 130-row mutation and validation workload. An experimental build
adds timestamps and identities; awaited mode joins the same return operation.
Production source files and public APIs are unchanged. This is repeat-run
prediction with mode-conditioned services, not counterfactual policy ranking.
No policy search, concurrency matrix, new backend or runtime controller belongs
to this experiment.

## Measurements and identities

Build one experimental executable from pinned production sources, with retained
exact source transformations at the ledger boundaries. Record a monotonic clock
and frequency, request identity (phase/ordinal), reservation identity, and unique
reset-operation identity. Capture request, queued acquisition, admission, reset
request, permit acquisition, reset service completion, clean publication and
permit release separately. Link reservation identity to the database observed
by its lease. Record external request/acquisition/body-end/receipt boundaries.
Publication/admission events occur under the ledger lock; release timestamp is
taken at the semaphore release boundary. Describe the timestamp's side of each
operation and any instrumentation cost. Never infer these identities by zipping
completion order. Preserve all traces and detect missing/duplicate events.

Warm every database, verify simultaneous lease isolation, baseline contents,
database OIDs and sequence state, then perform 32 warmup cycles. Each measured
run contains 256 tests. Verify baseline and mutation distribution each cycle,
and join every physical reset, permit and background wrapper at suite end.
Final audit and fixture disposal remain separate. Record startup, preparation,
warmup, final audit, disposal, full process wall, host CPU/load/pressure and
available Docker resource observations. CPU/I/O demand is still not identified
by host observations; do not turn those observations into a fitted contention
multiplier.

Before measuring, both modes must pass a short native smoke, an intentional
workload-distribution failure with cleanup, trace identity/conservation checks,
and a deterministic held-reset control showing no dirty reuse and complete
drain. Measure instrumentation event-collection overhead separately; disclose
that the predictive claim concerns the instrumented executable.

## Prospective split and execution order

Collect five complete paired rounds, using additional async waits 0/25/100/500
ms, 256 measured tests, 32 warmups, eight slots, four test/reset workers, one
preparer, four administrator connections, four connections/database, one 1 GiB
PostgreSQL 16 Alpine host per case. Retain image, source, executable, contract
and process identities, actual settings and resource inventories. There is no
policy or capacity variation.

Rounds 0–2 are calibration (24 runs). Rounds 3–4 are prospective validation
(16 runs), collected **after** fitting and freezing all predictions. All earlier
TT-038 runs and first-attempt validation data are development information and
cannot be a fresh holdout. No test or reset from validation enters fitting.
Rotate waits by round: [0,25,100,500], [25,100,500,0], [100,500,0,25],
[500,0,25,100], [0,25,100,500]. Within each round/wait, awaited goes first when
(round + wait-position) is even, background otherwise. Run cases serially; no
extra rounds, selective omissions, result-driven reruns or outlier deletion.
Retain unsuccessful attempts and stop collection for a correctness/identity or
resource-cleanup failure. Repairs require a new source/contract binding, with
the affected cohort explicitly superseded rather than silently continued.

Commit the final qualified harness, model and contract before calibration.
Freeze hashes of calibration inputs, fit, forecast and sources before starting
validation. Validate collection integrity without feeding held-out timings back
into model selection. A failed predictive gate remains a failed experiment.

## Model and sampling fixed before fitting

Use the existing integer-microsecond event kernel, stable event order, FIFO
acquisition and FIFO reset approximation (SemaphoreSlim fairness is not a
native guarantee). Distinguish cleanup before reset submission, body execution,
reset service, publication tail, release tail and return receipt. Background
receipt may overlap reset execution; awaited receipt follows complete reset.
Generate queueing from resource contention; never sample observed queue waits
as service. A clean slot may be reused before its previous reset releases its
permit. Final completion joins tests, physical resets and release tails.

For each mode/wait and each of 64 seeds 39000–39063, select **one entire run**
from the three calibration runs. All service components come from that run.
Within each worker, draw circular blocks of eight joint test/reset records,
preserving matched body/reset/cleanup/receipt/publication/release durations.
Cross-worker short-term correlation remains unmodelled. Draw complete joint
records, not independent component values. Each seed is reused across strata.
No fitted queue time, throughput multiplier, validation sample, outlier removal
or result-dependent sampling change is allowed.

Service definitions: body = external body-end minus acquisition; cleanup =
reset-request minus body-end; checkout overhead = external checkout minus
actual queued-for-clean duration; reset execution = service-end minus
permit-acquired; publication tail = clean-publication minus service-end;
release tail = permit-release minus clean-publication. For background mode,
receipt is measured from body-end independently of reset start. For awaited
mode, receipt tail is measured from reset completion. Nonnegative clock
quantization is permitted only within one timestamp tick, not to hide an
invalid ordering. Record inter-request worker overhead as a separate dispatch
service; record initial worker request offsets from the warm-suite origin.

## Prediction gate (unchanged allowances)

Predict each held-out run with its stratum's median over the 64 replicates;
retain descriptive 5th/95th nearest-rank prediction bounds. Compare medians over
the two whole validation runs in each stratum. Every stratum metric and every
individual suite time must pass. A descriptive interval is not a calibrated
confidence interval, especially with two validation runs.

| Metric | Absolute allowance | Relative allowance |
| --- | ---: | ---: |
| Warm suite including physical reset drain | 0 ms | 10% stratum; 20% per run |
| Mean checkout latency | 3 ms | 25% |
| p95 checkout latency | 10 ms | 25% |
| Mean reset-permit queue | 3 ms | 25% |
| p95 reset-permit queue | 10 ms | 25% |
| Fraction of checkouts >1 ms (legacy proxy) | 0.10 | 0% |
| Fraction of requests actually queued for a clean slot | 0.10 | 0% |
| Mean actual clean-slot queue duration | 3 ms | 25% |

Use max(absolute allowance, relative allowance × observed). Preserve signed
errors for every run, including calibration comparisons. Report physical final
drain and endpoint polling separately, without inventing a new threshold.
The legacy proxy remains overhead-inclusive; exact queued request events now
provide the additional clean-starvation metrics. Native trace conservation and
kernel deterministic controls are correctness gates, distinct from prediction.

Run the six original sensitivities at the measured capacity: body ×0.8/1.2,
reset execution ×0.8/1.2, extra release tail 2 ms and block size one. They probe
assumptions, not measured contention laws. Report complete runtime, observed
unmodelled costs, instrumentation limitations and every negative outcome.

If the gate fails, retain results and diagnose against this frozen design; do
not tune the model or thresholds to this holdout. A later revision needs newly
frozen whole-run validation data. If it passes, qualification applies only to
this instrumented workload/configuration; any counterfactual policy sweep and
physical-finalist budget still need a separately declared bounded scope.

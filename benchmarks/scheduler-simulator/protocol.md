# TT-039 reference-model protocol

Investigation for [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).
GitHub owns live status. This protocol is frozen before fitting or inspecting
individual held-out runs; the public TT-038 aggregate results were already known.
This is a retrospective holdout, not a blind prospective experiment.

## Inputs and split

`inputs.json` pins SHA-256 identities of the corrected TT-038 v4 contract, source
and runtime identity records, every case report and process receipt, and the
published archive/manifest. It assigns **whole paired rounds 0–2** to calibration
(24 runs), and **rounds 3–4** to validation (16 runs). Each split contains both
modes at every wait. No individual test crosses the split. Earlier OID-query
failure and order-confounded v3 remain retained in the original archive and are
excluded by this prespecified v4 selection, not removed or relabelled.

The archived harness and sources define the measurement contract. Source hashes
identify the original executions; this model does not execute those binaries.
Verification rejects changed/missing inputs and an incomplete or overlapping
split. Extracting the already published archive supplies the same inputs offline.

## Model before fitting

Use an integer-microsecond virtual clock, stable event ordering, FIFO clean slots
and acquisition waiters, and FIFO reset admission. Acquisition FIFO is explicit
in the production pool; reset FIFO approximates `SemaphoreSlim` and is not its
fairness guarantee. Model closed-loop test workers, exclusive database ownership,
dirty-slot reset queues, bounded execution, verified clean-slot publication,
separate permit-release tails, and final physical drain. Deterministic controls
must exercise timing, saturation, FIFO admission, no early dirty-slot reuse,
publication before permit release, and complete resource conservation.

At the observed capacity only (8 slots, 4 test workers, 4 reset workers), fit
separate empirical service samples per mode/wait using calibration runs only:

- body: `Returning - Acquired`, retaining the synthetic wait and actual DB work;
- checkout overhead: first four checkouts per run, when all slots were warm;
- cleanup/handoff: background `Returned - Returning`, used in both modes because
  awaited return already contains reset queue/service and cannot separate cleanup;
- reset execution: recorded execution duration, which already includes validation.

For each of 64 replicates, draw one entire calibration run per component. Sample
body sequences in circular blocks of eight within each worker; sample resets in
circular completion-order blocks of eight. Checkout/handoff sample empirically.
Body and reset streams are independent: reset events have no test/database IDs;
never zip them to ordinals or claim joint correlation was recovered. Fixed seeds
39000–39063 are reused across cases. No fitted throughput multiplier, queueing
duration or held-out service sample enters predictions. Background handoff is
approximated as preceding reset admission although real work can overlap it.
The baseline permit-release tail is zero; this unmeasured tail remains explicit.

## Metrics and gate declared before fitting

For every validation run compare observed metrics with the median of 64 virtual
replicates. Retain 5th/95th nearest-rank prediction bounds and all signed errors.
For each mode/wait stratum also compare medians across its two held-out runs.
Accept a metric when its absolute error is at most the larger of its absolute
and relative allowance below. **Every stratum metric and every per-run suite
time must pass**; one failure withholds qualification. Prediction bounds are
descriptive, not calibrated confidence intervals with two held-out runs.

| Metric | Absolute allowance | Relative allowance |
| --- | ---: | ---: |
| Suite completion, including physical reset drain | 0 ms | 10% stratum; 20% per run |
| Mean checkout latency | 3 ms | 25% |
| p95 checkout latency (nearest rank) | 10 ms | 25% |
| Mean reset-permit queue wait | 3 ms | 25% |
| p95 reset-permit queue wait | 10 ms | 25% |
| Fraction of checkouts exceeding 1 ms | 0.10 | 0% |

These are engineering usefulness thresholds: suite predictions should resolve
roughly 10% effects, while queue tolerances avoid relative errors exploding near
zero. Effects within model error cannot justify policy selection. Checkout >1 ms
is an **operational starvation proxy**, including publication/context overhead;
it is not exact clean-slot starvation. Report exact virtual queue time separately.
Sampled Ready/queued states cannot establish exact physical starvation duration.
Report final drain separately without a pass threshold: the last few resets and
physical settlement polling are particularly sensitive to unobserved tails.

## Uncertainty, scope and stopping rule

Report startup, preparation/preflight, final audit, disposal and complete process
wall as observed, unmodelled costs. Predict only the warm pipeline. Reset event
timestamps occur after publication/release/logging; do not derive precise starts
or slot availability by subtracting durations. Settlement polling and callback
overhead remain unmodelled endpoint costs; no reset duration is added twice.

Run prespecified sensitivities at the same capacity: body ×0.8/1.2, reset ×0.8/1.2,
permit-release tail 2 ms, and independent (block size one) service sampling.
These are assumption probes, not measured contention laws. Traces do not measure
separate CPU/I/O demand, and concurrency changes can alter service distributions.
Do not extrapolate fixed service times to arbitrary capacity.

Qualification supports repeated-run prediction for the two existing modes at
this configuration only. Mode-conditioned service distributions do **not** prove
counterfactual policy ranking, a shared runtime policy implementation, or general
capacity planning. No public metadata API or runtime scheduler changes are part
of this slice. Keep the offline model small; extract shared scheduling decisions
only when an accepted real-executor slice can validate their common contract.

If the gate fails, retain the negative result and do not search policies, widen
thresholds, or tune against these held-out runs. Name the narrow missing evidence
and require new whole-run validation data for a revised predictive claim. Only
after qualification may a follow-up predeclare a bounded policy sweep, uncertainty
requirements and a small physical-finalist budget; no physical matrix is started
by this protocol.

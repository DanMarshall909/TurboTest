# Prospective scheduler reference qualification

Investigation for [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).
GitHub owns live status. **Revision 2 failed prospective prediction qualification;
policy search remains gated.** All 40 native cases / 10,240 measured tests passed
correctness, trace integrity and cleanup; these facts do not qualify predictions.

The [first attempt](../tt039/report.md) remains unchanged, including its failed
awaited/0 ms prediction gate. Revision 2 keeps matched test/reset records from
one whole calibration run and measures the boundaries that the earlier traces
could not identify. It predicts successful repeated runs at one configuration;
it does not establish counterfactual policy ranking.

## Prospective prediction result

All 16 individual suite predictions meet the frozen 20% allowance. Six of eight
suite strata meet 10%; awaited/0 ms (+10.21%) and background/25 ms (+11.38%) fail.
Background/0 ms also fails actual clean-starvation fraction: observed 0.74023,
predicted 0.84180, an absolute difference of 0.10156 against 0.10. Overall, 61 of
64 stratum metric checks pass. The gate requires all checks, so it fails. No
sampling, thresholds, split or forecasts were changed after seeing these data.

| Mode | Added wait | Observed suite | Predicted suite | Signed error | 10% suite gate |
| --- | ---: | ---: | ---: | ---: | --- |
| Awaited | 0 ms | 6.094 s | 6.717 s | +10.21% | Fail |
| Awaited | 25 ms | 7.576 s | 7.649 s | +0.97% | Pass |
| Awaited | 100 ms | 12.274 s | 12.267 s | -0.06% | Pass |
| Awaited | 500 ms | 37.608 s | 37.859 s | +0.67% | Pass |
| Background | 0 ms | 4.209 s | 4.233 s | +0.57% | Pass |
| Background | 25 ms | 4.430 s | 4.934 s | +11.38% | Fail |
| Background | 100 ms | 8.899 s | 8.757 s | -1.60% | Pass |
| Background | 500 ms | 34.048 s | 34.158 s | +0.32% | Pass |

Complete run/stratum errors and physical-versus-wrapper drain are retained in
[evaluation.json](evaluation.json). The saved fit, forecasts, calibration samples,
prediction bounds and six sensitivities are retained in the raw evidence archive.
Fitting took 0.358 s; all forecasts and sensitivities took 13.555 s. The prediction
freeze at `24adde27293f5e2d217ec399fff6922865f935a9` pins 227 calibration/forecast
artifacts and predates every validation run. Evaluation deliberately exits 2 for
a completed negative gate. This is different from a missing-input/usage error.

## Post-result diagnostic and remaining problem

A separate post-hoc replay feeds each run’s own observed services into the
unchanged event kernel in original within-worker order. Its maximum absolute
suite error across all 40 runs is 0.73%; maximum clean-starvation error is
1.18 percentage points. This **uses validation service samples**
and is neither a forecast nor a replacement qualification gate. The frozen
prospective result remains FAIL. The diagnostic suggests the timing kernel and
measured boundaries can explain recorded execution; it does not prove which
sampling or host-variation assumption caused the prospective errors.

Revision 2 retains whole-run cost conditioning but independently resamples each
worker’s blocks, losing shared chronology within that run. Preserving complete
cross-worker trajectories is a concrete next hypothesis. Any later revision
needs a new protocol and fresh whole-run validation data; these observed runs
cannot remain a fresh holdout. No policy sweep or physical-finalist set is
selected by this failed experiment.

## Frozen design and qualification

[Protocol](../../../../../benchmarks/scheduler-simulator/protocol-r2.md),
[collection contract](../../../../../experiments/tt039/contract.json), model,
probe and [qualification binding](../../../../../experiments/tt039/collection-binding.json)
were frozen locally at `d19174075ad4a657f70ec3864fb4f88cf49c38a1`, before any
measured calibration case. The binding names the exact native/runtime/image
identities and 112 qualification artifacts. Rounds 0–2 supply calibration;
rounds 3–4 are fresh validation collected only after the forecasts are frozen.
The old TT-038 runs and first-attempt validation are development information.

Both modes use one experimental executable compiled from copied current
production sources. Timestamp/identity probes are isolated from `src/`.
Awaited mode joins that same current return operation. It is a new comparison,
distinct from the older TT-038 awaited baseline DLL. There is no new backend,
feature disabling, public metadata API or production scheduling policy.

The physical workload remains eight databases, four test workers, four reset
permits, one preparer, four administrator connections, four connections per
database and a single 1 GiB PostgreSQL 16 Alpine host. Each case warms and audits
all databases, performs 32 warmups, then executes 256 measured tests with the
unchanged seeded schema, 130-row mutation and baseline/mutation/sequence checks.
Cases run serially with frozen rotated waits and alternating mode order.

Two native smokes and two intentional workload-distribution failures passed
their success/failure and cleanup controls. Both held-reset controls passed:
the dirty database remained unavailable, a separate clean database served a
lease, exhausted demand waited, and fixture shutdown joined the held cleanup.
Every successful traced case passes explicit identity/conservation checks before
another case begins. Source and runtime bytes are checked before and after each
case; validation launch also requires a committed forecast binding.

## What the measurements distinguish

Each measured request has its own phase/ordinal identity. Admission carries
reservation identity, which binds to the lease's actual database and OID.
Every reset has a separate operation GUID. The reader rejects absent, duplicate,
aliased or misordered boundaries, overlapping dirty-slot ownership, impossible
definite permit occupancy, incomplete physical ledger state and abandoned
wrappers. It never matches resets to tests by completion order.

Dirty transition, admission and clean publication are recorded under the ledger
lock. Permit release is bracketed before/after the actual semaphore call; a new
reset can acquire within that interval. The model samples release from the
before-bound, reports bracket widths, and retains the uncertainty. Prepared
callback completion and background wrapper completion are separate events.
Physical drain, wrapper drain and the final settlement/worker-join tail are
reported separately. The legacy checkout >1 ms proxy remains overhead-inclusive;
actual queued request events supply the new clean-slot starvation metrics.

Every replicate selects one entire calibration run per mode/wait. Circular
blocks of eight retain joint body/reset/cleanup/receipt/publication/release
samples within each worker. Initial request offsets and inter-request dispatch
costs come from the same run. Queueing is generated by the virtual resource
ledger. Background receipt can overlap reset execution; publication can precede
release; completion joins reset and wrapper tails. Cross-worker short-term
dependence remains unmodelled.

The original 10% stratum/20% individual suite allowances and queue/proxy
allowances are unchanged. Actual clean-starvation fraction adds a 0.10 absolute
allowance; actual mean clean queue adds max(3 ms, 25%). Every metric and every
individual suite must pass. Sixty-four fixed seeds supply descriptive 5th/95th
prediction bounds, not calibrated confidence intervals.

## Instrumentation and limits

In a separate single-thread collection probe, enabled capture took about
0.094 µs/event across three batches of 50,000 events. All cold/disabled timings
are retained. Three paired background trace-on/off controls gave warm-suite
ratios 1.0112, 1.0042 and 0.8405. They do not isolate a consistent overhead effect.
Both variants still contain the experimental identity/scoping code; these
controls do not prove equivalence to an uninstrumented production binary.
The prediction claim applies to the instrumented executable.

Startup, preparation/warmup, final audit, disposal and process wall are observed,
unmodelled costs. Host CPU/load/pressure, actual PostgreSQL settings, container
limits and resource inventories are retained. They do not identify separate
CPU/I/O demand or a contention law. Mode-conditioned costs at one capacity do
not support arbitrary concurrency changes or counterfactual policy ranking.
The six predeclared sensitivity probes remain assumption checks.

An evidence-output collision in the first held-control wrapper overwrote its
native JSON with a supervisor receipt. Those receipts remain diagnostic history.
The final controls were rerun with distinct `control.json` and
`control-process.json` names; both outputs and terminal statuses are verified in
the frozen qualification binding. No measured run was collected before repair.

## Reproduction and verification

The [simulator README](../../../../../benchmarks/scheduler-simulator/README.md)
and [native experiment instructions](../../../../../experiments/tt039/README.md)
give exact commands and lane selections. Twenty-two fast tests pass in about
0.5–0.6 s. The revision CLI boundary contract passes, including missing-forecast
rejection, calibration-only reconstruction, frozen prediction identity and
preservation of existing outputs. The original archive contract still reproduces
its negative qualification result. Both slow selections run in CI.

Deterministic timing controls establish separate receipt, publication, release
and wrapper completion, including an independently calculated 39 µs background
pipeline versus a 55 µs awaited pipeline. Synthetic anti-correlated run controls
reject impossible cross-run service combinations. Altering every validation
service cannot change the fitted calibration distributions; an intentionally
ten-times-slower validation cohort fails against the original frozen forecast.

[The evidence manifest](evidence-manifest.json) pins all 727 retained files and
87 offline model inputs in the [raw archive](raw-evidence.tar.gz) (3.64 MiB).
The archive contract rebuilds the entire calibration-only fit and reproduces
every saved evaluation field except runtime, including the failed gate. It uses
no Docker or native binaries. Raw native traces, source snapshots/patches,
identities, controls, unsuccessful development attempts and the explicitly
post-hoc diagnostic remain retained. Compiled binaries and shared caches stay
local with their identities; they are not needed for offline reproduction.

[Verification](verification.json) records 22 fast checks in 0.556 s and all three
slow checks passing. Fast line tracing executes 96.6% of the kernel, 86.6% of
the original adapter and 87.7% of the revised adapter. Kernel gaps are invariant
failure guards; adapter gaps are CLI paths exercised by separate subprocess
contracts. These are line-execution figures, not branch or assertion coverage.
The complete forecast/identity test dominates the fast lane but keeps ordinary
feedback below a second; full real-archive reconstruction remains in slow.
Production refactoring reuses the existing metric/error calculations and one
resource kernel; test refactoring shares only trace construction, with hand-
derived timing and anti-correlation oracles. No policy engine duplication was
introduced.

[Cleanup](cleanup.json) confirms all 40 measured processes stopped, per-case
resource inventories matched, and all 60 task-owned native container prefixes
(including development/qualification cases) were absent. Shared dependencies,
PostgreSQL images, retained runtimes and prior TT-039/TT-038 evidence remain.
The owned PostgreSQL output volume had 130.55 GiB free when measured, above the
7 GiB reserve-plus-growth requirement. Task retention remains below its 1 GiB
budget. Archive/CLI temporary extractions are removed on completion.

A fresh [temporary-file probe](mutation-environment.json) confirms `cp --reflink=always`
is unsupported on the task volume; its owned directory was removed. Post-session
mutation remains pending, with no full-copy fallback or mutation score claimed. Independent completion review and hosted CI outcomes belong to the
linked issue and remain distinct from the failed prediction qualification.
Recovery is published production `2ba887f`; source/forecast freezes are `d191740`
and `24adde2`.

# Scheduler reference-model calibration

Investigation for [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).
GitHub owns live status. This record retains a completed reference-model attempt;
**predictive qualification failed, so policy search remains gated.**

The virtual pipeline passes deterministic timing, FIFO, exclusive ownership,
bounded reset capacity, clean publication and final-drain controls. Against the
held-out TT-038 runs, seven of eight mode/wait strata meet the suite-time error
limit, and all queueing/starvation-proxy metrics pass. Awaited/0 ms fails: its
median predicted suite is **21.3% too long**, beyond the frozen 10% stratum limit.
One individual run is 24.4% too long, beyond the 20% per-run limit. No thresholds,
split, fitted services or sampling method were tuned after observing this result.

## Frozen experiment and prediction errors

[Protocol](../../../../../benchmarks/scheduler-simulator/protocol.md) and
[input identities](../../../../../benchmarks/scheduler-simulator/inputs.json)
were committed at `6f7298d2e92748920409859c6194e5b21cebd880` before fitting.
Calibration uses complete paired rounds 0–2 (24 runs); validation uses complete
rounds 3–4 (16 runs), retaining both modes and all four waits in each split.
The public TT-038 aggregate report was already known: this is a retrospective
holdout, not a blind prospective test. The original OID-query failure and v3
order-confounded attempt remain untouched in the TT-038 evidence archive.

Each prediction is the median of 64 fixed-seed replicates. Each stratum below
compares the medians of its two whole held-out runs. Complete signed errors,
descriptive 5th/95th prediction bounds, calibration-run comparisons and all six
sensitivity probes are in [evaluation.json](evaluation.json). The
[saved fit](fit.json) contains only calibration service distributions. A test
changes all validation timing data and proves those fitted distributions do not
change; evaluation also rejects a saved fit that differs from calibration-only
reconstruction.

| Mode | Added wait | Observed suite | Predicted suite | Signed error | 10% gate |
| --- | ---: | ---: | ---: | ---: | --- |
| Awaited | 0 ms | 6.346 s | 7.700 s | +21.33% | Fail |
| Awaited | 25 ms | 7.662 s | 7.896 s | +3.06% | Pass |
| Awaited | 100 ms | 12.672 s | 12.490 s | −1.44% | Pass |
| Awaited | 500 ms | 37.758 s | 38.427 s | +1.77% | Pass |
| Background | 0 ms | 4.192 s | 4.488 s | +7.05% | Pass |
| Background | 25 ms | 4.755 s | 4.834 s | +1.64% | Pass |
| Background | 100 ms | 8.737 s | 9.523 s | +9.00% | Pass |
| Background | 500 ms | 34.942 s | 34.227 s | −2.05% | Pass |

All 40 stratum queue/proxy checks pass their declared absolute-or-relative
tolerances. Maximum absolute stratum errors are 4.12 ms mean checkout, 5.72 ms
p95 checkout, 3.58 ms mean reset-permit wait, 5.95 ms p95 permit wait, and 0.0801
for the fraction of checkouts exceeding 1 ms. The two mean errors over 3 ms pass
their relative allowance. Passing these checks does not override the suite gate.

## What this model demonstrates

The kernel uses an integer-microsecond virtual clock and stable event order.
Closed-loop workers request another lease after their return receipt. A clean
slot goes to the oldest acquisition waiter; a dirty slot queues for a bounded
reset permit. Verified publication and permit release are separate events, so
the next test may use a clean slot before the previous reset wrapper releases
its permit. Completion requires every test, physical reset and release tail to
settle, with every slot clean and no outstanding permit or queue. Reset FIFO is
an explicit approximation around `SemaphoreSlim`, not a promised native fairness
property. Failed-reset recovery is outside these successful-run traces.

The model samples service execution separately from generated queueing. Body
streams retain within-worker blocks of eight; reset streams retain completion-
order blocks of eight. Whole calibration runs supply each component's samples.
Reset events have no test or database identifiers, so independent body/reset
draws do not claim to preserve per-test correlation. Checkout overhead is
estimated from each run's first four warm checkouts. Background cleanup/handoff
is used for both modes because awaited return already contains reset execution.
Reset validation is already in recorded execution and is never added again.

Deterministic controls include an analytically calculated 41 μs saturated
two-slot/one-reset-worker pipeline, a 31 μs publication-before-permit-release
case, zero-duration simultaneous events, mixed durations/capacities, both modes,
invalid timing/capacity rejection, exact worker sequencing and complete drain.
These establish simulation semantics; they do not establish native performance.

## Limitations and next evidence

The failed awaited/0 group includes a particularly slow calibration run:
round 0 took 11.039 s, versus 5.784/5.736 s in calibration rounds 1/2 and
6.189/6.503 s in validation rounds 3/4. Body/reset means in round 0 were
58.66/112.34 ms, versus approximately 29–32/57–60 ms in the other calibration
runs. Independent component-run draws can mix shared run-level conditions;
uncontrolled host variation and lost dependence are plausible limitations, not
an established causal explanation. Do not discard that slow run to pass the gate.

At the same capacity, scaling body or reset execution by ±20% shifts median
suite predictions by up to about 20%. A 2 ms permit-release tail shifts them by
at most 2.4%; block-size-one sampling shifts them by up to 3.2%. These probes
show sensitivity to assumptions. They do not measure CPU/I/O demand or predict
contention at other capacities. Costs are mode-conditioned; even successful
repeat-run prediction would not establish counterfactual policy ranking.

Checkout >1 ms is an operational starvation proxy, including context/publication
overhead. Exact physical clean-slot starvation cannot be recovered from these
traces or their 20 ms pool snapshots. Reset event timestamps are logged after
publication/release, so subtracting execution does not reveal exact reset starts.
Background handoff may overlap reset start; the model serializes that small cost.
Unmeasured release tails, callback overhead and settlement polling remain explicit
limitations. Final drain is reported separately without an accuracy claim.

Startup, preparation/preflight, final audit, fixture disposal, harness total time
and complete process wall are retained per run as **observed, unmodelled costs**.
The warm endpoint includes physical resets; it is not a process-time prediction.
No runtime controller, public metadata API, reset backend or production scheduling
policy changed. Sharing policy code with a real executor remains a later design
goal, requiring a common contract and native evidence.

The next bounded decision is how to preserve observed whole-run dependence and
measure the remaining timing boundaries. A revised protocol should keep body
and reset samples conditioned on the same recorded run, then predeclare new
whole-run validation data. Targeted traces should identify test/database/reset
relationships and request/admission, reset start, clean publication and permit
release separately, while recording concurrent host pressure. The current
validation runs are now observed and cannot serve as a fresh holdout. No policy
sweep or physical-finalist budget is selected until a reference model qualifies.

## Verification and reproduction

The [simulator README](../../../../../benchmarks/scheduler-simulator/README.md)
gives exact offline fit/validate commands and named fast/slow/all test selections.
The first fit took 0.085 s; all baseline predictions and six sensitivity probes
took 9.300 s on Python 3.10.12. The negative gate intentionally exits 2 after
writing its evaluation. No Docker resources were allocated.

Fifteen fast tests passed in 0.168 s ([timings](final-fast.json),
[output](final-fast.log.txt)). The slow archive/CLI contract reproduces the exact
negative gate from the published archive in about 10 s; it also rejects a missing
fit and proves existing output files survive reruns
([output](final-slow.log.txt), [process receipt](final-slow.json)). Both selections run in CI.
The existing ten verification-driver tests also pass. Production .NET inputs
are unchanged; their latest source-identical correctness/pack evidence is
[CI 41432126343](https://github.com/DanMarshall909/TurboTest/actions/runs/41432126343).
New hosted validation and independent completion-review outcomes belong to the
linked issue, separately from predictive qualification.

RED evidence for [pipeline semantics](ac1-red.log.txt), [pinned calibration](ac2-red.log.txt),
[prediction gates](ac3-red.log.txt) and [saved-fit identity](ac2-fit-identity-red.log.txt)
is retained. Fast standard-library line tracing covers 95% of the kernel and
86% of the fitting/evaluation module. Kernel gaps are invariant-failure guards;
adapter gaps are CLI paths exercised by the separate archive contract without
subprocess coverage attribution. This is line execution evidence, not branch or
assertion coverage. Production refactoring kept the pure event kernel separate
from trace handling and centralized latency/gate calculations; test refactoring
shares only input construction, with independent hand-derived timing oracles.

Post-session mutation remains pending: a verified temporary-file probe reports
`cp --reflink=always: Operation not supported` on the available task volume.
No safe CoW sandbox was established, no full-copy fallback or mutation score is
claimed, and the probe's owned temporary directory was removed. This does not
block ordinary verification or publication. The retained attempt is roughly
0.4 MiB, within the existing 2 GiB verification retention budget. Shared caches,
native runtimes, historical traces and reusable builds are preserved.

Recovery is the preceding production master `bc310dc`; the freeze checkpoint is
`6f7298d`. This slice adds an independently reproducible, negative calibration
attempt. Simulator credibility remains unfinished work on TT-039.

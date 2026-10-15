---
id: TT-010
title: Measure pool latency and correct performance claims
status: Resolved
priority: P1
type: Feature
created: 2026-10-14
depends_on: [TT-005, TT-006]
---

# TT-010: Measure pool latency and correct performance claims

## Problem

The README advertises PostgreSQL "50ms reset" and reset under 100 ms without defining workload or timing boundaries. The existing benchmark/test can also pass by using a fresh container instead of verifying reuse. The experiments show queueing, data volume, preparation and shutdown alter the outcome; parking moves deletion cost rather than removing it.

Source: [README](../../../../README.md), [tests](../../../../tests/TurboTest.Tests/BasicTests.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Add maintained metrics and a reproducible public-API benchmark. Define the warm 50 ms aspiration and report startup, checkout, reset-to-ready, full cycle and final drain separately. Correct unsupported README claims immediately using the existing evidence; integrated measurements follow the pool prerequisites.

## Acceptance criteria

- [x] Metrics distinguish queue wait, checkout/preparation/rewarm, executing reset, reset-permit wait, validation, cycle and final shutdown, plus state/capacity counts and scale reasons. They omit credentials and test payloads.
- [x] A workload check independently rejects zero rows or an incorrect distribution before timing. Capture source revision/hashes, runtime/image identity, fixture schema/seed, row mix, concurrency, sample counts and timing/percentile definitions.
- [x] Maintained cases include nonempty mutated/deleted seeds, 130/1,300/13,000-row loads, fixed capacities and 1→8→1 plus repeated bursts. Assert actual database reuse and cross-database marker absence.
- [x] Order is randomized/counterbalanced and repeated as appropriate; startup warmth and resource budgets are explicit. All deferred cleanup and teardown are awaited and reported.
- [x] Reports show p50/p95/max, throughput, resource footprint and failures, including fixed-mode results that beat adaptation. Per-slot rewarm latency cannot substitute for request checkout or full-cycle latency.
- [x] Fast controller/workload tests and targeted PostgreSQL correctness regressions run in CI. Slow performance experiments have an explicit supported invocation/job; do not install a universal flaky 50 ms CI threshold.
- [x] README/API docs define supported providers and lifecycle, scope the performance target to named workloads, and remove unconditional reset guarantees unsupported by measurements.

## Verification

Land metrics and a fixed-pool benchmark before TT-009 consumes them. Add adaptive/parking comparisons when those implementations exist; this is a follow-up within this ticket, not a prerequisite cycle. Keep reproducible raw results and rejected-run diagnostics. Baseline evidence is local exploratory data, not a portable release gate.

## Implementation log

- **2026-10-14 — partial documentation correction:** Removed unconditional
  under-100-ms/50-ms reset claims and unsupported provider timings from README,
  provider API documentation and package description. README now defines the
  workload-specific warm aspiration, links to the prototype evidence and
  identifies the current lease/DI ownership defects. Public lifecycle migration
  documentation, maintained metrics and reproducible integrated benchmarks
  remain open. No production performance claim is earned by TT-002's current
  correctness checks.
- **2026-10-15 — metrics implementation in progress:** Payload-free structured
  event 402 distinguishes admitted public checkout, the shared return receipt,
  full cycle and actual factory shutdown. The existing ledger records queue
  residence through assignment/cancellation/rejection, preparation and rewarm
  permit wait/execution, row reset permit wait/execution, recreation and recovery
  deletion. Prepared validation is a subset of execution, with recreation
  validation separate from row validation. Snapshots accompany event 402.
  Diagnostic observers cannot change outcomes. Native REDs exposed event 400
  logger exceptions replacing success/material failure and execution including
  recovery; both are repaired. Deterministic component checks and PostgreSQL
  seed/session/identity controls pass. The complete fast lane passes 163 checks
  without failures/skips; the selected native/timing gate passes 16 at its source
  cut. A held context confirms full shutdown of 5s cleanup + 7s pool = 12s.
  Raw RED/GREEN/coverage commands, TRX and source bindings are retained under
  `artifacts/tt-010`. Scale reasons await TT-009; whole-suite validation, review
  and publication remain pending.
- **Maintained workload in progress:** A solution-owned benchmark project now
  defines literal 100/20/10,1000/200/100 and 10000/2000/1000 inserted row mixes,
  separately from the mutated retained parent seed. Fast checks reject empty,
  missing, misdistributed and unsupported loads. A native RED rejects a no-op
  loader as `WorkloadEmpty`; the implemented public-lease workload verifies
  actual counts, mutated/deleted seeds, next identities 101/201/301, unchanged
  database/OID reuse and marker absence in the simultaneous second database.
  Empty/wrong-mix rejection controls use actual queries, with further seed/foreign
  marker controls under validation. The runner entry point explicitly reports
  that it is unfinished; no performance result is claimed. Scheduling, reporting,
  source/runtime/image/resource bindings, deferred cleanup, supported invocation
  and fixed/adaptive comparison evidence remain. TT-010 is Open.
- **Current checkpoint:** All 181 fast checks and 19 selected native
  timing/workload/recovery checks pass without failures/skips. Statistics use
  conventional median p50 and nearest-rank `ceil(.95*n)` p95, retain zero-count
  results with unavailable percentiles, and reject negative/nonfinite samples.
  Seed and foreign-marker rejection controls pass against real PostgreSQL.
  A further genuine RED found synchronous cancellation could emit a queue
  diagnostic under the ledger lock; observation now begins outside that lock,
  with assignment/cancellation timestamps still captured atomically. Full
  lifecycle/benchmark completion, review and commit remain pending.
- **Maintained runner and fixed/parking experiment:** The supported Release
  command and manual performance workflow run counterbalanced capacities 1/2/4/8
  across two blocks. Each case independently verifies seeds, sequences, actual
  row counts, baseline restoration and known database/OID reuse; simultaneous
  multi-slot leases verify marker isolation. Source snapshots/hashes, loaded
  assembly/runtime/SDK identities, cached/actual image ID, PostgreSQL version,
  resource budgets, raw timings/events and sampled memory/state are retained.
  Workers, lease returns, monitor stop and fixture cleanup are awaited. The first
  frozen experiment (`artifacts/tt-010/fixed-parking-experiment-r1`) exits zero
  after about 706 seconds: all 16 cases and 2,544 cycles pass, with 53 samples per
  row size per case, no failures and complete owned cleanup. All 4,000 copied
  source hashes and counterbalanced equivalent plans verify independently.
  Sampled cgroup memory includes cache and is a lower bound on peak usage; it
  does not establish worktree/build memory requirements. These local observations
  are not portable latency guarantees or adaptive comparisons.
- **Audit repairs:** Two real native REDs reproduced empty/idle-only cases
  incorrectly reporting success; they now persist `CaseWorkload` rejection
  before fixture startup. Six fast REDs reproduced acceptance of unequal,
  negative, nonfinite, oversized or timed-phase idle delays; the comparison
  validator now rejects them as `ScheduleTiming`. The generated frozen experiment
  avoided both faults; its source cut remains historical rather than being
  relabelled as the repaired source. Both criterion GREEN/coverage/refactor
  records are retained. Current fast/whole-suite verification, independent slice
  review and local commit remain pending. TT-009 and the adaptive comparison
  continue after the metrics/fixed-benchmark slice; TT-010 remains Open.
- **Current slice verification:** The repaired candidate passes all 198 fast
  checks and all 356 whole-suite checks without failures/skips. Solution and
  separate `DynamicScalingExample` builds pass with zero warnings/errors. Both
  builds and the whole suite bind the same compiler inputs. Independent
  reconstruction of all 72 saved benchmark timing summaries from raw samples
  matches count, median p50, nearest-rank p95 and maximum. The
  [slice archive](../../evidence/2026-10-15/pool-metrics-fixed-benchmark/manifest.json)
  retains terminal commands, raw logs/TRX/coverage, historical source bindings,
  rejected runs and current qualifications. Independent slice review and commit
  remain pending; adaptive implementation/comparisons and scale reasons remain.

## Slice decision log

- **2026-10-15 — Metrics and fixed/parking benchmark slice accepted:** Independent
  completion review passed for the exact candidate and finite administrative
  envelope. The current whole suite and both builds pass; the frozen repeated
  Release comparison retains all16 successful cases and2544 measured cycles.
  [Slice evidence](../../evidence/2026-10-15/pool-metrics-fixed-benchmark/manifest.json)
  preserves raw RED/GREEN/coverage, source cuts, rejected-run diagnostics and
  independent statistics checks. This is the accepted dependency slice described
  in Verification, not resolution of TT-010. Scale reasons, TT-009 implementation,
  evidence-qualified defaults and equivalent adaptive comparisons remain. Whole
  coding-session mutation remains pending its final immutable committed snapshot.

## Final adaptive comparison candidate

- **2026-10-15:** Event 403 now exposes safe scale reasons, target/state, memory age
  and actual maintenance latency/outcome. The public prepared controller owns
  sampling and admitted work through final drain. The supported `--adaptive`
  command and manual workflow compare matched fixed/adaptive startup two with an
  already-warm fixed eight control over two counterbalanced blocks. Preflight leases
  only the startup count; verified new identities are bounded by the physical
  cap and known OIDs must remain stable. Reports retain phase states and raw
  first/renewed/repeated/heavy/idle results, failures and resource samples.
- The Release experiment passes all six cases and 954 timed cycles. Fixed capacity
  wins burst latency in both blocks; adaptive heavy throughput is mixed. Raw
  cgroup peaks include cache and do not prove resident-memory savings. Defaults
  are conservative choices rather than an optimal-window claim. See the
  [comparison and rationale](../../evidence/2026-10-15/adaptive-pool-final/results.md).
- The current candidate passes all 267 fast and 434 whole-suite checks without
  failures/skips. Both final builds and durable archive verification pass. Exact independent
  completion review passed for the candidate and resolution envelope. The prior metrics/fixed-parking
  slice archive remains unchanged; the new archive binds its own source cut.

## Final decision log

- **2026-10-15 — Resolved:** Independent completion review passed for the exact
  implementation candidate and finite administrative envelope. All 434 current
  tests pass, including 267 fast checks, without failures/skips; both builds pass
  without warnings/errors. The six-case 954-cycle adaptive comparison and earlier
  fixed/parking evidence retain actual reuse/isolation checks, fixed wins,
  resource limitations and qualified defaults. The
  [final evidence](../../evidence/2026-10-15/adaptive-pool-final/manifest.json)
  binds raw commands/results, source cuts, coverage and reconstruction checks.
  Whole-session mutation remains a post-commit follow-up against the final
  immutable source snapshot; it is pending, not passing evidence.

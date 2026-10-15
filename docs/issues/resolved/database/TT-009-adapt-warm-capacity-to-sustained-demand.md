---
id: TT-009
title: Adapt warm capacity to sustained demand
status: Resolved
priority: P2
type: Feature
created: 2026-10-14
depends_on: [TT-008, TT-010]
---

# TT-009: Adapt warm capacity to sustained demand

## Problem

A fixed small pool queues under bursts; holding the maximum warm pool has ongoing resource costs. The prototype grew 2→8 and parked back to two active slots, but ramp-up still hurt first/renewed burst latency. The measured controller thresholds are experimental, not tuned defaults.

Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md). Fixed-two peak checkout p95 was 629.86 ms; adaptive observations were 142.28–223.96 ms; already-prepared eight was 81.38 ms in that run. Run order/cache effects limit causal comparisons.

## Scope

Implement a small deterministic controller for warm capacity, driven by sustained demand and measured lifecycle/resource state. Keep reset strategy selection separate. Support fixed mode, explicit minimum/maximum, a startup warm count/concurrency hint and configurable stabilization windows.

## Acceptance criteria

- [x] Sustained queue/wait pressure can grow warm capacity within physical, connection and resource budgets; brief spikes do not repeatedly trigger growth.
- [x] Shrink requires sustained spare capacity and chooses only eligible Ready slots. Cooldowns/hysteresis prevent repeated grow/shrink oscillation through short lulls.
- [x] Prefer parked rewarm; count pending preparations, quarantines and retirements in cap decisions. No concurrent decision can overreserve.
- [x] Executing reset saturation and resource headroom have an explicit policy, so queue pressure is not treated as proof that another database will improve throughput.
- [x] Missing/stale resource readings have a documented conservative fallback and diagnostic. CPU measurements, if used, have defined normalization; raw multi-core percentages cannot masquerade as a 0–100% utilization budget.
- [x] Fixed mode disables adaptive changes without changing lease/reset correctness. Validate invalid limits and combinations at initialization.
- [x] A controllable monotonic clock drives pure tests for growth, stabilization, cooldowns, floors/caps, memory pressure, retained-cap rewarm and interrupted pressure windows.
- [x] Scale decisions expose reason/state/latency; a real public-API 1→8→1→idle→8 workload verifies growth, eligible parking, reuse and final drain. Select and document defaults from TT-010 evidence rather than copying 100/200/750 ms prototype constants.

## Verification

Compare fixed and adaptive modes with equivalent workloads and startup conditions. Retain first-burst, repeated-burst, sustained-heavy-load and idle results, including resource cost and cases where adaptation loses. Do not require universal 50 ms latency or claim optimal settings for every load.

## Implementation progress

- **2026-10-15 — Policy and initial ledger growth:** A monotonic policy now
  requires continuous queue pressure for growth and continuous eligible spare
  capacity for shrink. Both directions share a cooldown; interrupted windows
  restart even between observations. Warm minimum/maximum, pending preparation
  and rewarm, unavailable physical reservations, full reset/shared-cleanup
  saturation and missing/invalid/stale/over-threshold memory all constrain
  growth. Eligible shrink does not require an available memory reading.
  Literal boundary tests use distinct 5/7/11-second windows and controlled
  40/79/80-percent readings; they do not select portable policy defaults.
- The existing slot ledger now gates fresh and retained claims by a separate
  warm target, counts promised preparation/rewarm before backend work, and
  atomically applies admitted growth. Eight overlapping ticks with held
  verification create only one additional reservation and cannot publish the
  lease early. Shared cleanup occupancy is independently visible in snapshots
  and calibrated by a held real component parking callback. The current fast
  gate passes **218 checks without failures/skips**; scoped RED/GREEN/coverage
  and refactor evidence is retained under `artifacts/tt-009`. The earlier
  356-test whole-suite result belongs to the committed TT-010 slice, not these
  uncommitted changes.
- **2026-10-15 — Internal shrink and monitoring:** Stabilized shrink now
  claims eligible Ready slots through the existing parking/retirement path.
  Ready exhaustion restarts spare timing; captured Ready floors remain
  protected. Retained adaptive growth at a full physical cap waits for identity
  verification rather than allocating a replacement. Captured configuration
  validates policy limits/windows, including the supported timer range.
  One-shot statistics from the actual owned Docker host calibrate working-set
  conversion and canceled reads. Monitor component tests pass for canceled
  sampling, joining admitted maintenance, unavailable readings and retaining
  late material failures. These focused source-bound results are retained
  under `artifacts/tt-009`; they are not a whole-suite result.
- **2026-10-15 — Public integration and drain:** Prepared initialization now
  captures and enables the controller after baseline sealing and startup warming.
  A public 1→8→1→idle→8 PostgreSQL scenario passes with original OIDs, restored
  seeds/sequences and an empty final owner. Explicit maintenance preserves the
  admitted target; recreation/retry retains a warm commitment through deletion.
  Exhausted background growth retains unseen errors after caller cancellation.
  Native held-growth shutdown and late automatic-retirement controls pass;
  monitor failures are aggregated after physical cleanup. Event 403 reports safe
  reason, target/state, resource age and actual maintenance duration/outcome;
  failed realization is distinguished from admitted growth. The most recent
  complete fast checkpoint passed 253 checks before these later focused runs.
- **2026-10-15 — Final implementation candidate:** All 267 fast checks and 434
  whole-suite checks pass without failures/skips. The repeated Release comparison
  passes all six cases and 954 cycles, with common source/assembly/image identities,
  equivalent startup two conditions and a warm fixed eight control. Independent
  reconstruction verifies 1,680 source snapshots and 225 timing summaries. Fixed
  capacity wins burst latency in both blocks; adaptation uses 3–4 identities and
  parks back to target two. The [results and default rationale](../../evidence/2026-10-15/adaptive-pool-final/results.md)
  retain those costs and limitations. Independent completion review passed for the exact candidate and resolution
  envelope. Both final builds and durable archive verification pass.

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

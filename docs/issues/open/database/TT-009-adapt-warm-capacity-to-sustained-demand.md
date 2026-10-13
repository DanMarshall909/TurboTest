---
id: TT-009
title: Adapt warm capacity to sustained demand
status: Open
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

- [ ] Sustained queue/wait pressure can grow warm capacity within physical, connection and resource budgets; brief spikes do not repeatedly trigger growth.
- [ ] Shrink requires sustained spare capacity and chooses only eligible Ready slots. Cooldowns/hysteresis prevent repeated grow/shrink oscillation through short lulls.
- [ ] Prefer parked rewarm; count pending preparations, quarantines and retirements in cap decisions. No concurrent decision can overreserve.
- [ ] Executing reset saturation and resource headroom have an explicit policy, so queue pressure is not treated as proof that another database will improve throughput.
- [ ] Missing/stale resource readings have a documented conservative fallback and diagnostic. CPU measurements, if used, have defined normalization; raw multi-core percentages cannot masquerade as a 0–100% utilization budget.
- [ ] Fixed mode disables adaptive changes without changing lease/reset correctness. Validate invalid limits and combinations at initialization.
- [ ] A controllable monotonic clock drives pure tests for growth, stabilization, cooldowns, floors/caps, memory pressure, retained-cap rewarm and interrupted pressure windows.
- [ ] Scale decisions expose reason/state/latency; a real public-API 1→8→1→idle→8 workload verifies growth, eligible parking, reuse and final drain. Select and document defaults from TT-010 evidence rather than copying 100/200/750 ms prototype constants.

## Verification

Compare fixed and adaptive modes with equivalent workloads and startup conditions. Retain first-burst, repeated-burst, sustained-heavy-load and idle results, including resource cost and cases where adaptation loses. Do not require universal 50 ms latency or claim optimal settings for every load.

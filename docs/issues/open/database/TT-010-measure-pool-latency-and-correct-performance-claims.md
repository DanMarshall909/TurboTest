---
id: TT-010
title: Measure pool latency and correct performance claims
status: Open
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

- [ ] Metrics distinguish queue wait, checkout/preparation/rewarm, executing reset, reset-permit wait, validation, cycle and final shutdown, plus state/capacity counts and scale reasons. They omit credentials and test payloads.
- [ ] A workload check independently rejects zero rows or an incorrect distribution before timing. Capture source revision/hashes, runtime/image identity, fixture schema/seed, row mix, concurrency, sample counts and timing/percentile definitions.
- [ ] Maintained cases include nonempty mutated/deleted seeds, 130/1,300/13,000-row loads, fixed capacities and 1→8→1 plus repeated bursts. Assert actual database reuse and cross-database marker absence.
- [ ] Order is randomized/counterbalanced and repeated as appropriate; startup warmth and resource budgets are explicit. All deferred cleanup and teardown are awaited and reported.
- [ ] Reports show p50/p95/max, throughput, resource footprint and failures, including fixed-mode results that beat adaptation. Per-slot rewarm latency cannot substitute for request checkout or full-cycle latency.
- [ ] Fast controller/workload tests and targeted PostgreSQL correctness regressions run in CI. Slow performance experiments have an explicit supported invocation/job; do not install a universal flaky 50 ms CI threshold.
- [ ] README/API docs define supported providers and lifecycle, scope the performance target to named workloads, and remove unconditional reset guarantees unsupported by measurements.

## Verification

Land metrics and a fixed-pool benchmark before TT-009 consumes them. Add adaptive/parking comparisons when those implementations exist; this is a follow-up within this ticket, not a prerequisite cycle. Keep reproducible raw results and rejected-run diagnostics. Baseline evidence is local exploratory data, not a portable release gate.

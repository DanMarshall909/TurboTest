---
id: TT-012
title: Bound mutation liveness diagnosis and failed-test cleanup
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-012: Bound mutation liveness diagnosis and failed-test cleanup

## Problem

All111 timeouts are mapped to exact mutated progress/permit/protocol/provider/diagnostic paths. Source inspection explains candidate mechanisms; only the startup-signal representative is independently reproduced. Removing monitor start completion makes the existing test exceed its2s observation wait and then hang in its unbounded finally Join until the external10s guard stops it.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: T01, T02, T03, T04, T05, T06, T07-TtCapacityMonitor, T07-TtSlotPool, T07-Tt, T07-TtFactory (111 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Use bounded diagnostics to attribute remaining timeout clusters to a named failing assertion, mutation-induced nontermination or a runner/selection limitation; retain raw Timeout status until proved otherwise.
- [ ] Make representative liveness regressions fail under an explicit external deadline while preserving actual resource settlement and all existing assertions.
- [ ] Exercise completion-signal, permit, retry/index progress, failure-construction and provider-diversion representatives; preserve meaningful separate end-to-end evidence.
- [ ] Measure baseline/build/coverage/execution separately before tuning; do not increase timeouts or concurrency solely to change the score.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

---
id: TT-015
title: Cover benchmark command options and workload diagnostics
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-015: Cover benchmark command options and workload diagnostics

## Problem

Missing command-level mode/calibration/output assertions allow distinguishable survivors. Default output-path portability has no established tested contract.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B04, B05, B17 (13 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Reject incompatible plan/smoke and unknown fault values before allocating resources; assert a useful named argument diagnostic.
- [ ] Assert accepted calibration invocations and fixed/adaptive/smoke/experiment output labels.
- [ ] Define a safe discoverable default destination or require explicit output; cover portable filename behavior.
- [ ] Distinguish all-zero WorkloadEmpty from nonzero partial-empty WorkloadDistribution observations.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

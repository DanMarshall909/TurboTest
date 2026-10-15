---
id: TT-013
title: Assert complete schedule generation and validation domains
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-013: Assert complete schedule generation and validation domains

## Problem

Existing valid schedule tests use two blocks, and several negative validator tests are independently rejected by a later peer guard. A real546 single-fault reproduction passes all13 existing schedule checks but rejects the valid three-block input that succeeds unchanged.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B06, B07, B08, B10, B11, B12 (44 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Accept supported equal/end boundary values and at least three blocks; independently assert later-pair rotation and reversal.
- [ ] Reject malformed RequireValid shape, timing and local distribution even when every peer is changed consistently.
- [ ] Reject peer-order differences while preserving valid per-peer row distributions.
- [ ] Decide owned seeded shuffle/randomization properties before asserting an exact incidental seed arithmetic constant.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

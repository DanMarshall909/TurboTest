---
id: TT-021
title: Close Docker adapter cancellation and permit gaps
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-021: Close Docker adapter cancellation and permit gaps

## Problem

Selected evidence does not distinguish genuine deletion errors from404 absence, canceled stats, a failed initialization followed by permit reuse, or the cold-image requested identity.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A03 (4 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Calibrate404 confirmed absence against genuine deletion failure using the real client contract.
- [ ] Propagate canceled stats observation as cancellation rather than unavailable memory.
- [ ] After failed initialization, prove another attempt can acquire and release the same budget.
- [ ] Capture the requested cold image identity; preserve existing real Docker cleanup/readiness tests outside mutation.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

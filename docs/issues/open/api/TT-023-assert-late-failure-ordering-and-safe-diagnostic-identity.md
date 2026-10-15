---
id: TT-023
title: Assert late failure ordering and safe diagnostic identity
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-023: Assert late failure ordering and safe diagnostic identity

## Problem

Existing tests often start drain before releasing failed work, leaving the ordering observer-canceled -> actual failure completed -> drain-start unasserted. Timeout/cancellation, compound compensation and public lifecycle stage distinctions are also underasserted.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A06, A07, A09, L07, L13 (54 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Cancel a maintenance observer, finish actual failed work, then start drain; retain its meaningful safe failure exactly once and confirm physical cleanup.
- [ ] Distinguish initializer-owned timeout, observation deadline, caller cancellation and simultaneous shutdown cancellation.
- [ ] Hold initialization and cancellation callbacks separately and retain concurrent callback/cleanup/drain failures with original stages, SQLSTATE and cleanup capabilities.
- [ ] Preserve already-safe lifecycle errors and all meaningful secondary failures without asserting arbitrary prose or order.
- [ ] Exercise repeated monitor Stop/Join with throwing or blocking reader cancellation callbacks and once-only error delivery.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

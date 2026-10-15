---
id: TT-018
title: Verify executable signal cancellation and final cleanup
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-018: Verify executable signal cancellation and final cleanup

## Problem

Program signal handler, cancellation forwarding, command invocation and event unsubscription have no ordinary coverage.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B21 (5 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Run an ordinary bounded process-level Ctrl+C scenario that requests cooperative cancellation.
- [ ] Assert the command waits for owned cleanup, returns the documented failure outcome and removes its signal subscription.
- [ ] Keep this end-to-end check explicitly outside mutation selection.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

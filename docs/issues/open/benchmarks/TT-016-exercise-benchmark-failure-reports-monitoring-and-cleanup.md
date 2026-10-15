---
id: TT-016
title: Exercise benchmark failure reports monitoring and cleanup
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-016: Exercise benchmark failure reports monitoring and cleanup

## Problem

No ordinary tests drive several runner rejection, compensating return/drain and deferred-cleanup retry paths. Initial resource sampling alone satisfies the current nonempty resource assertion.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B18, B19, B20 (29 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Inject image/source/identity/marker/workload failures at the minimum owning boundary and require named unsuccessful report outcomes.
- [ ] Prove compensating return, final drain and deferred cleanup settle, retain all material failures and obey bounded retry counts.
- [ ] Observe recurring resource samples across a controlled long phase and stop monitoring before final drain.
- [ ] Keep native correctness checks outside mutation; add only inexpensive owning-rule/component tests where they preserve the real contract.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

---
id: TT-022
title: Assert lifecycle admission publication and drain races
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-022: Assert lifecycle admission publication and drain races

## Problem

Several startup barriers, closure fences and cancellation-before-publication branches lack distinct fast oracles. Empty-provider disposal does not establish ownership of actual pending startup and queued lookups.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A02, A05, A08, A11 (23 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Use synchronously reentrant user factories/cancellation callbacks to prove shared work is published before callbacks.
- [ ] Reject precanceled and late scope/factory/provider admission under the accepted public contract.
- [ ] Hold startup/acquisition work independently, including a database acquired before lease publication; require drain to wait until ownership and cleanup settle.
- [ ] Keep nuanced lease reentrancy behind the inner database barrier as a bounded reachability investigation until publicly proved.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

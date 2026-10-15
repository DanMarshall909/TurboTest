---
id: TT-017
title: Verify metadata command cancellation and process cleanup
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-017: Verify metadata command cancellation and process cleanup

## Problem

SourceBinding process helper is only tested through successful Git/dotnet calls. Nonzero exit, cancellation, process-tree termination, stderr completion and null-start fallback need differentiated evidence.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B02, B16 (15 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Use a bounded deterministic process fixture to reject nonzero exit and propagate caller cancellation.
- [ ] Confirm the exact owned process tree stops and both output streams settle on success/failure/cancellation.
- [ ] Investigate supported reachability and consequences of the nullable-start fallback and omitted stderr await before adding an oracle or simplifying.
- [ ] Keep incidental process exception wording outside the owned contract.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

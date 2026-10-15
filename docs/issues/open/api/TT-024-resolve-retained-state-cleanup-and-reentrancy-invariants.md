---
id: TT-024
title: Resolve retained-state cleanup and reentrancy invariants
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-024: Resolve retained-state cleanup and reentrancy invariants

## Problem

30 API and18 ledger questions include CTS/registration lifetime, retained completed entries, dominated database fallback, cleanup-capability precedence and optional internal callback domains. These are not declared equivalent or confirmed product bugs.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A15, L21, L22, L23 (48 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Establish externally observable bounded retained-state/resource lifetime contracts for CTS, registrations and completed work entries.
- [ ] Prove or disprove dominated database fallback and optional warm-target/demand-observation invariants; simplify only where supported contracts permit.
- [ ] Define and reproduce cleanup-capability ownership/precedence when explicit, causal and secondary handles differ.
- [ ] Use gated callbacks and strict external deadlines for reentrant drain and late-cancellation ordering; avoid reflection-only private collection assertions.
- [ ] Resolve each linked question with evidence, an accepted contract decision or a narrower tracked item before removing useful code.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

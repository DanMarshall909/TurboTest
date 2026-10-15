---
id: TT-020
title: Assert default DI registration and observable options
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-020: Assert default DI registration and observable options

## Problem

Fast scope tests install an existing factory, masking default/context-only registration. Sensitive logging and the documented default container prefix are not independently asserted.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A01, A10 (9 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Resolve context-only/default registrations successfully and preserve an explicitly registered custom factory identity.
- [ ] Assert configured/default sensitive logging through meaningful options/configuration output.
- [ ] Assert documented resource prefix behavior without pinning random identifiers.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

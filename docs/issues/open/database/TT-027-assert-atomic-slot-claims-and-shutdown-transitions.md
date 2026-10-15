---
id: TT-027
title: Assert atomic slot claims and shutdown transitions
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-027: Assert atomic slot claims and shutdown transitions

## Problem

Normal paths and final deletion assertions miss several transition-specific races, retained-capacity and permit ownership distinctions. Sequential acquisition masks1431 because preparation synchronously re-adds the placeholder.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: L09, L10, L11, L12, L15, L20 (42 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Prove warm commitments with spare physical headroom and concurrent claims across reservation-to-preparation publication.
- [ ] Cover explicit retained limit, in-flight parking, mixed idle expiry and blocked retirement deletion during direct drain.
- [ ] Assert admission/publication after stop, precanceled Ready acquisition, cancellation before permit acquisition and later shared permit reuse.
- [ ] Exercise inter-tick Ready/demand interruption through actual ledger cancellation/publication/maintenance paths.
- [ ] Cover the linked terminal/detached-cancellation/recovery-delay/recreation-stop/publication race branches with observable ownership oracles.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

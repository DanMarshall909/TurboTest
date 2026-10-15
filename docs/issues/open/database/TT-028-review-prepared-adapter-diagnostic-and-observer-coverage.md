---
id: TT-028
title: Review prepared adapter diagnostic and observer coverage
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-028: Review prepared adapter diagnostic and observer coverage

## Problem

Fast valid-idle setup intentionally fails before host allocation and accepts any lifecycle exception. It cannot prove actual prepared lifecycle actions, successful drain, monitor admission or reset observer callbacks. Ordinary PostgreSQL tests already protect many of those contracts.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: L14, L17 (36 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Retain and explicitly map existing PostgreSQL event401, parking, retry, adaptive, capacity and connection evidence to the linked variants.
- [ ] Close only independently confirmed remaining callback/outcome gaps, including successful queue Outcome and reset observeWait semantics, at their owning layer.
- [ ] Preserve documented event401 operation, duration and success fields; do not classify them as unowned prose.
- [ ] Keep slow/end-to-end evidence out of mutation and do not broadly exclude mixed nodes or mock away the actual lifecycle rule.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

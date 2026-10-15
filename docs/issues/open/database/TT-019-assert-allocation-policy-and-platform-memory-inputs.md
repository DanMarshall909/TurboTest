---
id: TT-019
title: Assert allocation policy and platform memory inputs
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-019: Assert allocation policy and platform memory inputs

## Problem

Resource allocation calculations execute in logging tests without independent numeric oracles; the ordinary fixture clamps allocation to one. Platform/parser differences can therefore remain invisible.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A04 (31 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Provide deterministic resource inputs and assert reserves, conservative CPU/memory bottlenecks, configured caps and per-container memory clamps using input-derived expectations.
- [ ] Cover parser/configuration selection, unit conversion and supported fallback behavior without freezing incidental debug messages.
- [ ] Retain calibrated real platform/client boundary evidence; do not infer one platform is covered by another.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

---
id: TT-026
title: Assert adaptive prepared and budget equality boundaries
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-026: Assert adaptive prepared and budget equality boundaries

## Problem

Invalid-neighbor tests leave accepted configuration, memory, timer and budget endpoints unasserted; pure policy tests also miss some fresh-window and named-reason distinctions.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: L04, L05, L06, L08, L16 (32 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Accept exact supported adaptive/prepared endpoints including UTF8 byte limits, startup/min/max/floor equality, cooldown0, maximum timer, age==interval and memory1/100.
- [ ] Assert zero/equal/long.MaxValue memory and cache inputs against independent availability/constraint expectations.
- [ ] Assert fresh per-step stabilization when cooldown is shorter and distinguish occupied maintenance without backlog.
- [ ] Assert captured ordinary/prepared budget defaults and double-start monitor rejection without Docker.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

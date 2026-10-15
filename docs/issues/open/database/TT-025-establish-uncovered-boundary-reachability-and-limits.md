---
id: TT-025
title: Establish uncovered boundary reachability and limits
status: Open
priority: P2
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-025: Establish uncovered boundary reachability and limits

## Problem

Uncalled scalar helper, catalog limit/duplicate-owner guards, repeated initialization/process fallback and owned workload identity mismatch need supported-domain or reachability proof.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A13, A14, L19 (10 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Identify accepted callers before covering, wiring or removing the uncalled scalar helper.
- [ ] Own catalog resource limits and duplicate-owner rejection under a reachable supported scenario, or document a proved invariant.
- [ ] Probe repeated/concurrent initialization and process completion/fallback contracts with bounded inputs.
- [ ] Establish whether owned database/user identity mismatch is reachable through supported external catalog changes or a calibrated boundary before adding tests or exclusions.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

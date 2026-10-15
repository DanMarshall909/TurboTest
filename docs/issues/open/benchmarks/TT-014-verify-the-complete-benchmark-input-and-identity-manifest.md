---
id: TT-014
title: Verify the complete benchmark input and identity manifest
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-014: Verify the complete benchmark input and identity manifest

## Problem

The current binding fixture supplies only src/Model.cs and a solution. It does not independently enumerate all maintained roots/configuration inputs or isolate metadata drift.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: B13, B14, B15 (29 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Assert the exact maintained source, project, build, package, tool, test-runner and workflow input set from independently specified fixtures.
- [ ] Exclude generated bin/obj content and prove repeated capture cannot capture its own snapshots, including evidence beneath a scanned root.
- [ ] Reject missing maintained source and individual Head, DirtyState, SDK and Runtime drift.
- [ ] Preserve source bytes, hashes, unique assembly identities and parseable snapshot references; do not pin unowned display ordering.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

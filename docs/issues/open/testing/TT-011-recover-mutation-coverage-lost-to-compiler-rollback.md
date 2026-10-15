---
id: TT-011
title: Recover mutation coverage lost to compiler rollback
status: Open
priority: P1
type: Quality
created: 2026-10-15
depends_on: []
---

# TT-011: Recover mutation coverage lost to compiler rollback

## Problem

The full reports classify265 mutants as CompileError. Exact standalone compiler probes find137 semantic errors,106 with no unsuppressed diagnostics and22 warning-policy-sensitive variants. Whole-member rollback therefore hides potentially useful faults; no unexecuted variant is credited as killed.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: C-TurboTest.EFCore-clean, C-TurboTest.Benchmarks-clean, C-TurboTest.EFCore-warning, C-TurboTest.Benchmarks-warning (128 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Acceptance criteria

- [ ] Preserve genuine invalid mutants as compiler rejections and document pinned compiler/warning-policy identity.
- [ ] Reproduce CE223 with the named monitor test; recover executable candidates using a bounded supported Stryker configuration, targeted upstream fix or calibrated adapter without weakening source scope or assertions.
- [ ] For all106 clean and22 warning-sensitive candidates, record recovered actual outcomes or exact diagnosed tool limitations; never relabel diagnostic-only results as executed.
- [ ] Retain full eligible fast-only runs, source/test attribution and owned CoW cleanup; no broad exclusion or threshold reduction.

## Verification

Use the smallest observable contract and preserve genuine one-fault rejection evidence. Keep existing ordinary fast/slow lanes, source identities and assertions. A future repair follows normal behavior-first TDD and proportionate regression gates; diagnostic probes and mutation scores are not a whole-suite pass.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

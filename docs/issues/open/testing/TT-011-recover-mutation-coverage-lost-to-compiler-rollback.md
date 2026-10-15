---
id: TT-011
title: Recover mutation coverage lost to compiler rollback
type: Quality
created: 2026-10-15
github_issue: https://github.com/DanMarshall909/TurboTest/issues/1
---

# TT-011: Recover mutation coverage lost to compiler rollback

## Problem

The full reports classify265 mutants as CompileError. Exact standalone compiler probes find137 semantic errors,106 with no unsuppressed diagnostics and22 warning-policy-sensitive variants. Whole-member rollback therefore hides potentially useful faults; no unexecuted variant is credited as killed.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: C-TurboTest.EFCore-clean, C-TurboTest.Benchmarks-clean, C-TurboTest.EFCore-warning, C-TurboTest.Benchmarks-warning (128 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Live work

[TT-011 on GitHub](https://github.com/DanMarshall909/TurboTest/issues/1) owns the current acceptance criteria, priority, assignment and workflow status. The [robustness project](https://github.com/users/DanMarshall909/projects/15/views/2) owns sequencing and Kanban status.

This file retains the original investigation and evidence provenance. Its `open/` location means the investigation has not been archived; it does not mirror GitHub issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

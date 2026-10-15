---
id: TT-020
title: Assert default DI registration and observable options
type: Quality
created: 2026-10-15
github_issue: https://github.com/DanMarshall909/TurboTest/issues/10
---

# TT-020: Assert default DI registration and observable options

## Problem

Fast scope tests install an existing factory, masking default/context-only registration. Sensitive logging and the documented default container prefix are not independently asserted.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A01, A10 (9 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Live work

[TT-020 on GitHub](https://github.com/DanMarshall909/TurboTest/issues/10) owns the current acceptance criteria, priority, assignment and workflow status. The [robustness project](https://github.com/users/DanMarshall909/projects/15/views/2) owns sequencing and Kanban status.

This file retains the original investigation and evidence provenance. Its `open/` location means the investigation has not been archived; it does not mirror GitHub issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

---
id: TT-022
title: Assert lifecycle admission publication and drain races
type: Quality
created: 2026-10-15
github_issue: https://github.com/DanMarshall909/TurboTest/issues/12
---

# TT-022: Assert lifecycle admission publication and drain races

## Problem

Several startup barriers, closure fences and cancellation-before-publication branches lack distinct fast oracles. Empty-provider disposal does not establish ownership of actual pending startup and queued lookups.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A02, A05, A08, A11 (23 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Live work

[TT-022 on GitHub](https://github.com/DanMarshall909/TurboTest/issues/12) owns the current acceptance criteria, priority, assignment and workflow status. The [robustness project](https://github.com/users/DanMarshall909/projects/15/views/2) owns sequencing and Kanban status.

This file retains the original investigation and evidence provenance. Its `resolved/` location records the archive decision below; GitHub owns live issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

- **2026-10-30 — Archive decision:** [Source-bound lifecycle evidence](../../evidence/2026-10-30/tt022-lifecycle/results.md) covers precanceled and late admission, reentrant factory/ledger publication, acquired-before-lease drain, real provider startup with a queued lookup, and the publicly reachable lease return path. The maintained combined gate passed 365 fast and 176 PostgreSQL tests with matched Docker resource inventories. Independent whole-candidate review passed at `25606f87b0d6963fa34414e485ec17900db8bed5`; the documentation and tracking finalization is a bounded post-review envelope. Historical mutation statuses are unchanged.

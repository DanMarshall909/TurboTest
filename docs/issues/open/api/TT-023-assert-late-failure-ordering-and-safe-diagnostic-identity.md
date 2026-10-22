---
id: TT-023
title: Assert late failure ordering and safe diagnostic identity
type: Quality
created: 2026-10-15
github_issue: https://github.com/DanMarshall909/TurboTest/issues/13
---

# TT-023: Assert late failure ordering and safe diagnostic identity

## Problem

Existing tests often start drain before releasing failed work, leaving the ordering observer-canceled -> actual failure completed -> drain-start unasserted. Timeout/cancellation, compound compensation and public lifecycle stage distinctions are also underasserted.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A06, A07, A09, L07, L13 (54 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Live work

[TT-023 on GitHub](https://github.com/DanMarshall909/TurboTest/issues/13) owns the current acceptance criteria, priority, assignment and workflow status. The [robustness project](https://github.com/users/DanMarshall909/projects/15/views/2) owns sequencing and Kanban status.

This file retains the original investigation and evidence provenance. Its `open/` location means the investigation has not been archived; it does not mirror GitHub issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

- **2026-10-22 — Ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-ownership/manifest.json) records multi-owner recovery, explicit/causal/secondary composition, nested safe stage preservation and concurrent startup cancellation/drain/retry failures. The bounded slice has its independent verdict in `completion.json`. This investigation remains open for the remaining acceptance criteria and linked questions; GitHub owns live status.

- **2026-10-22 — Lifetime and reentry slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-lifetimes/manifest.json) records initializer/monitor cancellation-source lifetime, completed recovery payload release, pending registration release and direct reentrant drain publication. One redundant self-unregister is removed under a bounded producer/runtime proof; useful pending unregisters and reentry barriers remain. Final verification is 458 passing checks with 23 source-bound owning coverage rows. Both issues and the remaining linked questions stay open; the exact independent verdict is recorded in `completion.json`.

- **2026-10-22 — Factory ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/factory-ownership/manifest.json) records canceled-observer/actual-failure-before-drain ordering, abandoned acquisition cleanup rooting and completed context/lease/public-task release. The dominated final database fallback is removed; its useful dictionary ownership root remains. Final verification is 462 passing checks with15 owning coverage rows. Five additional bounded linked-question decisions leave28 unresolved; both investigations remain open. Independent verdict is recorded in `completion.json`; GitHub owns the exact partial acceptance checklist.

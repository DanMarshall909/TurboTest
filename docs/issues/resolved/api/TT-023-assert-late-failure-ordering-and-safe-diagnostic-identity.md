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

This file retains the original investigation and evidence provenance. Its `resolved/` location records investigation archival; it does not mirror GitHub issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

- **2026-10-22 — Ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-ownership/manifest.json) records multi-owner recovery, explicit/causal/secondary composition, nested safe stage preservation and concurrent startup cancellation/drain/retry failures. The bounded slice has its independent verdict in `completion.json`. This investigation remains open for the remaining acceptance criteria and linked questions; GitHub owns live status.

- **2026-10-22 — Lifetime and reentry slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-lifetimes/manifest.json) records initializer/monitor cancellation-source lifetime, completed recovery payload release, pending registration release and direct reentrant drain publication. One redundant self-unregister is removed under a bounded producer/runtime proof; useful pending unregisters and reentry barriers remain. Final verification is 458 passing checks with 23 source-bound owning coverage rows. Both issues and the remaining linked questions stay open; the exact independent verdict is recorded in `completion.json`.

- **2026-10-22 — Factory ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/factory-ownership/manifest.json) records canceled-observer/actual-failure-before-drain ordering, abandoned acquisition cleanup rooting and completed context/lease/public-task release. The dominated final database fallback is removed; its useful dictionary ownership root remains. Final verification is 462 passing checks with15 owning coverage rows. Five additional bounded linked-question decisions leave28 unresolved; both investigations remain open. Independent verdict is recorded in `completion.json`; GitHub owns the exact partial acceptance checklist.

- **2026-10-22 — Observer ordering and completed-node slice:** [Source-bound evidence](../../evidence/2026-10-22/factory-observer-race/manifest.json) records the maintained canceled-request/actual-failure/observer-finally race and one-off weak-node diagnostics for useful observer/worker/acquisition/drain removals. Eight additional bounded decisions leave20 of the original48 TT024questions unresolved. Production is unchanged. Final verification is463 passing ordinary checks and16 source-bound owning coverage rows; independent verdict is in `completion.json`. Both investigations remain open; original mutation statuses are unchanged and GitHub owns the partial acceptance checklist.

- **2026-10-22 — Preparation failure ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/preparation-failure-ownership/manifest.json) records unseen initial/replacement failures, retained prior and compensation errors, distinct successive errors, verified publication clearing, actual delivery deduplication and native ordinary cancellation classification. Useful retry-delay and stop predicates are retained with exact fault calibration, resolving two additional bounded original questions and leaving 18 of 48 unresolved. Final ordinary verification is 476 passes with 16 owning coverage rows; independent verdict is in `completion.json`. Both investigations stay open; original mutation statuses are unchanged and GitHub owns the partial acceptance checklist.

- **2026-10-22 — Late initializer failure ownership:** [Source-bound repair evidence](../../evidence/2026-10-22/startup-late-initializer/manifest.json) demonstrates a material initializer failure lost after startup observation was interrupted, then retains that safe error once through actual cancellation and physical cleanup. Six genuine fast RED cases and a separately fault-calibrated real PostgreSQL seed failure distinguish safe identity and confirmed one-time deletion from successful/owned-cancellation controls. Final ordinary verification is498passes and339owning coverage rows on identical source/runtime/dependencies; the independent bounded verdict is recorded in `completion.json`. This investigation and its remaining criteria stay open; no original mutation status or whole-ticket completion claim changes. Whole-session mutation remains pending; GitHub owns live status.

- **2026-10-22 — Diagnostic investigation archived:** [Final source-bound evidence](../../evidence/2026-10-22/lifecycle-diagnostic-closure/manifest.json) records all54original-question dispositions and all8accepted-criterion evidence, the demonstrated callback/child-cleanup repair, cancellation/timeout distinctions, compound safe diagnostics/capabilities and success-only maintenance worker simplification. Final ordinary verification is520passes, with383owning coverage rows and28current compiled fault calibrations. The independent whole-ticket verdict is retained in `completion.json`; GitHub owns checklist closure and live status. Original mutation statuses are unchanged, whole-session mutation remains pending, and native Project fields remain unverified/unmodified. This move archives the investigation rather than creating a second task board.

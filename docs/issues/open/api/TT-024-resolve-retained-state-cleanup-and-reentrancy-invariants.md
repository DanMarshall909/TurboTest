---
id: TT-024
title: Resolve retained-state cleanup and reentrancy invariants
type: Quality
created: 2026-10-15
github_issue: https://github.com/DanMarshall909/TurboTest/issues/14
---

# TT-024: Resolve retained-state cleanup and reentrancy invariants

## Problem

30 API and18 ledger questions include CTS/registration lifetime, retained completed entries, dominated database fallback, cleanup-capability precedence and optional internal callback domains. These are not declared equivalent or confirmed product bugs.

This tracks a review finding or explicit investigation, not a claim that each related mutant is a production bug.

## Evidence and scope

Source snapshot: `29e974adc59e36ef8622f5f2c176ddb563f8c6d4`; Stryker.NET5.0.0, fast-only267 tests, conservative228 slow-only ranges. Original ten implementation tickets remain resolved.

[Review](../../evidence/2026-10-15/mutation-review/review.md) and [exact mutant classifications](../../evidence/2026-10-15/mutation-review/classifications.json).

Related groups: A15, L21, L22, L23 (48 reported entries; classifications distinguish gaps, boundary evidence and investigation questions).

## Live work

[TT-024 on GitHub](https://github.com/DanMarshall909/TurboTest/issues/14) owns the current acceptance criteria, priority, assignment and workflow status. The [robustness project](https://github.com/users/DanMarshall909/projects/15/views/2) owns sequencing and Kanban status.

This file retains the original investigation and evidence provenance. Its `open/` location means the investigation has not been archived; it does not mirror GitHub issue status. The original acceptance checklist is preserved in Git history at `0f0ca8f` and was migrated to GitHub with the subsequent robustness-review refinements.

## Decision log

- **2026-10-15 — Opened:** Classified post-session evidence; accepted follow-up is a focused repair or bounded investigation under the criteria above. No production change is made by this review.

- **2026-10-15 — Migrated:** The user requested GitHub Issues and a Kanban project for all remaining work. GitHub now owns live planning and completion; this record retains durable investigation evidence. Completed repository tickets were left unchanged.

- **2026-10-22 — Ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-ownership/manifest.json) records multi-owner recovery, explicit/causal/secondary composition, nested safe stage preservation and concurrent startup cancellation/drain/retry failures. The bounded slice has its independent verdict in `completion.json`. This investigation remains open for the remaining acceptance criteria and linked questions; GitHub owns live status.

- **2026-10-22 — Lifetime and reentry slice:** [Source-bound evidence](../../evidence/2026-10-22/cleanup-lifetimes/manifest.json) records initializer/monitor cancellation-source lifetime, completed recovery payload release, pending registration release and direct reentrant drain publication. One redundant self-unregister is removed under a bounded producer/runtime proof; useful pending unregisters and reentry barriers remain. Final verification is 458 passing checks with 23 source-bound owning coverage rows. Both issues and the remaining linked questions stay open; the exact independent verdict is recorded in `completion.json`.

- **2026-10-22 — Factory ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/factory-ownership/manifest.json) records canceled-observer/actual-failure-before-drain ordering, abandoned acquisition cleanup rooting and completed context/lease/public-task release. The dominated final database fallback is removed; its useful dictionary ownership root remains. Final verification is 462 passing checks with15 owning coverage rows. Five additional bounded linked-question decisions leave28 unresolved; both investigations remain open. Independent verdict is recorded in `completion.json`; GitHub owns the exact partial acceptance checklist.

- **2026-10-22 — Observer ordering and completed-node slice:** [Source-bound evidence](../../evidence/2026-10-22/factory-observer-race/manifest.json) records the maintained canceled-request/actual-failure/observer-finally race and one-off weak-node diagnostics for useful observer/worker/acquisition/drain removals. Eight additional bounded decisions leave20 of the original48 TT024questions unresolved. Production is unchanged. Final verification is463 passing ordinary checks and16 source-bound owning coverage rows; independent verdict is in `completion.json`. Both investigations remain open; original mutation statuses are unchanged and GitHub owns the partial acceptance checklist.

- **2026-10-22 — Preparation failure ownership slice:** [Source-bound evidence](../../evidence/2026-10-22/preparation-failure-ownership/manifest.json) records unseen initial/replacement failures, retained prior and compensation errors, distinct successive errors, verified publication clearing, actual delivery deduplication and native ordinary cancellation classification. Useful retry-delay and stop predicates are retained with exact fault calibration, resolving two additional bounded original questions and leaving 18 of 48 unresolved. Final ordinary verification is 476 passes with 16 owning coverage rows; independent verdict is in `completion.json`. Both investigations stay open; original mutation statuses are unchanged and GitHub owns the partial acceptance checklist.

- **2026-10-22 — Final-resource disposal contracts:** [Source-bound evidence](../../evidence/2026-10-22/final-resource-disposal/manifest.json) records closed never-started/failed-start adapter admission and one-off owned SDK/provider source disposal diagnostics. Exact omissions reject the owning assertions with independent controls; useful disposals remain. Three more bounded questions leave15 of48 unresolved. Initial socket-precondition correction is retained without a leak claim; the provider handle is diagnostically requested and only successful held-callback drain is shown. Final ordinary verification is478 passes and2 owning coverage rows. Independent verdict is in `completion.json`; the investigation stays open and GitHub owns the partial checklist.

- **2026-10-22 — Context validation and cleanup ownership:** [Source-bound evidence](../../evidence/2026-10-22/context-validation-cleanup/manifest.json) protects adaptive zero-capacity diagnostic ownership and cancellation before optional schema initialization. Redundant ordinary completed-disposal state and prepared repeated-source fallback/registry are removed under exact producer/dependency proofs; all four lifecycle lock scopes remain. Five more bounded decisions leave10 of48 unresolved. The initial model-only oracle correction is retained without a product-defect claim. Final verification is480 ordinary passes and26 owning coverage rows; independent verdict is in `completion.json`. The investigation stays open, historical mutation statuses are unchanged and GitHub owns the partial checklist.

- **2026-10-22 — Queued retry and optional callback contracts:** [Source-bound evidence](../../evidence/2026-10-22/warm-callback-contracts/manifest.json) protects completed-timer retry cancellation and useful internal absent-rewarm, direct-evaluation and deferred-maintenance contracts. Repeated ClaimParked terminal checks are removed under actual caller/atomic queue-transition proof; capability/demand/admission checks remain. Six more bounded decisions leave4 of48 unresolved (466,1202,1208,1211). Initial FIFO/collector and reader-count corrections are retained separately. Final verification is484 ordinary passes and65 owning coverage rows; independent verdict is in `completion.json`. The investigation stays open, historical mutation statuses are unchanged and GitHub owns the partial checklist.

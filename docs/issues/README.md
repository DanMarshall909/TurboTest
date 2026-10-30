# TurboTest tickets

GitHub Issues is the authoritative tracker for remaining work in
[DanMarshall909/TurboTest](https://github.com/DanMarshall909/TurboTest/issues).
The [TurboTest robustness Kanban](https://github.com/users/DanMarshall909/projects/15/views/2) owns live priority,
sequencing and workflow status. Repository documents retain historical
investigations and evidence; completed tickets remain in their existing form.
Source code and historical evidence are published in the
[GitHub repository](https://github.com/DanMarshall909/TurboTest), alongside the
issues and linked project. This checkout tracks `origin/master`. Historical
evidence retains the publication limits recorded for its original run.

## Current status — 2026-10-30

All ten implementation tickets, TT-001 through TT-010, are Resolved. Independent
completion review passed for the final adaptive controller and performance work.
The table preserves implementation order and prerequisite identities.

TT-021 is also resolved: [PR #22](https://github.com/DanMarshall909/TurboTest/pull/22) merged its Docker adapter contract evidence, with 533 tests passing in [hosted CI](https://github.com/DanMarshall909/TurboTest/actions/runs/40996679200).

| Ticket | Work | Status | Priority | Prerequisites | Evidence |
| --- | --- | --- | --- | --- | --- |
| [TT-002](resolved/database/TT-002-reset-with-pooled-connections.md) | Reset safely with pooled PostgreSQL connections | Resolved | P0 | None | [Reset](evidence/2026-10-14/pooled-reset/manifest.json) |
| [TT-001](resolved/api/TT-001-own-and-release-database-leases.md) | Own and release public database leases | Resolved | P0 | TT-002 | [Public leases](evidence/2026-10-14/public-leases-repair/manifest.json) |
| [TT-003](resolved/database/TT-003-prepare-immutable-schema-and-seed-baselines.md) | Prepare immutable schema and seed baselines | Resolved | P1 | TT-001, TT-002 | [Baselines](evidence/2026-10-14/prepared-baseline-repair/manifest.json) |
| [TT-004](resolved/database/TT-004-restore-data-and-handle-schema-changes.md) | Restore data and handle schema changes | Resolved | P1 | TT-003 | [Restoration](evidence/2026-10-14/row-reset-progress/manifest.json) |
| [TT-005](resolved/database/TT-005-bound-fixture-owned-prepared-database-pools.md) | Bound fixture-owned prepared database pools | Resolved | P1 | TT-001, TT-003, TT-004 | [Capacity](evidence/2026-10-14/bounded-pool-final/manifest.json) |
| [TT-006](resolved/database/TT-006-quarantine-recover-and-drain-pool-resources.md) | Quarantine, recover, cancel and drain resources | Resolved | P1 | TT-005 | [Recovery](evidence/2026-10-14/resource-recovery-final/manifest.json) |
| [TT-007](resolved/api/TT-007-use-the-configured-owner-in-di-and-static-apis.md) | Fix DI and static lifecycle ownership | Resolved | P1 | TT-005, TT-006 | [DI ownership](evidence/2026-10-15/di-ownership-final/manifest.json) |
| [TT-008](resolved/database/TT-008-park-and-rewarm-prepared-databases.md) | Park and rewarm prepared databases | Resolved | P2 | TT-005, TT-006 | [Parking](evidence/2026-10-15/prepared-parking-final/manifest.json) |
| [TT-010](resolved/database/TT-010-measure-pool-latency-and-correct-performance-claims.md) | Measure latency and correct performance claims | Resolved | P1 | TT-005, TT-006 | [Metrics/fixed slice](evidence/2026-10-15/pool-metrics-fixed-benchmark/manifest.json), [final adaptive evidence](evidence/2026-10-15/adaptive-pool-final/manifest.json) |
| [TT-009](resolved/database/TT-009-adapt-warm-capacity-to-sustained-demand.md) | Adapt warm capacity to sustained demand | Resolved | P2 | TT-008, TT-010 | [Final adaptive evidence](evidence/2026-10-15/adaptive-pool-final/manifest.json) |

## Verification and performance

The maintained correctness gate covers **533 tests**, including **357 fast
checks**; failures and skips fail verification. It builds the solution and
separate DynamicScalingExample and records source-bound results. Earlier ticket
evidence retains its original source cut; use the hosted run for the exact
commit when assessing current verification.

The maintained Release experiments retain 16 fixed/parking cases and 2,544 cycles,
plus six fixed/adaptive cases and 954 cycles with equivalent startup conditions.
The [adaptive results and default rationale](evidence/2026-10-15/adaptive-pool-final/results.md)
retain fixed-mode wins, actual scale reasons, idle parking, resource observations
and limitations. The 50 ms aspiration concerns verified warm restoration of a
named workload; queueing, test body, startup and final drain are separate costs.
There is no universal latency threshold or optimal-window claim.

Whole-session mutation against committed production snapshot `29e974a` has now
completed and been reviewed. Both runs used fast tests only and calibrated
slow-only source exclusions. [The review](evidence/2026-10-15/mutation-review/review.md)
classifies all 403 survivors, 167 uncovered mutants, 111 timeouts and 265 compiler
errors. These are generated faults, not confirmed bug counts or a clean mutation
adequacy claim. The original [launch snapshot](evidence/2026-10-15/post-session-mutation/follow-up.json)
remains historical; the reviewed outcomes and new work below supersede its
pending action. Full raw reports and bounded direct checks are retained.

## Robustness work on GitHub

The original ten implementation tickets are Resolved. All remaining work is now
organized as **20 GitHub issues**: TT-011 through TT-028 retain their identities,
TT-029 adds reproducible verification, and TT-030 defines the final robustness
gate. The issue bodies include the subsequent lifecycle, platform-probe,
experiment-wide identity and reporting findings. A finding is not automatically
a reproduced production defect.

GitHub owns acceptance checklists, dependencies, priority, assignment and live
status. The project groups work into reliable verification, lifecycle ownership,
resource contracts, trustworthy benchmarks and final robustness evidence. Its
Order field gives recommended sequencing; native issue dependencies represent
whole-issue prerequisites. The final verification issue depends on every other
remaining item.

| Ticket | GitHub work item | Local investigation |
| --- | --- | --- |
| [TT-011](https://github.com/DanMarshall909/TurboTest/issues/1) | Recover mutation coverage lost to compiler rollback | [Evidence](open/testing/TT-011-recover-mutation-coverage-lost-to-compiler-rollback.md) |
| [TT-012](https://github.com/DanMarshall909/TurboTest/issues/2) | Bound mutation liveness diagnosis and failed-test cleanup | [Evidence](resolved/testing/TT-012-bound-mutation-liveness-diagnosis-and-failed-test-cleanup.md) |
| [TT-013](https://github.com/DanMarshall909/TurboTest/issues/3) | Assert complete schedule generation and validation domains | [Evidence](open/benchmarks/TT-013-assert-complete-schedule-generation-and-validation-domains.md) |
| [TT-014](https://github.com/DanMarshall909/TurboTest/issues/4) | Verify the complete benchmark input and identity manifest | [Evidence](open/benchmarks/TT-014-verify-the-complete-benchmark-input-and-identity-manifest.md) |
| [TT-015](https://github.com/DanMarshall909/TurboTest/issues/5) | Cover benchmark command options and workload diagnostics | [Evidence](open/benchmarks/TT-015-cover-benchmark-command-options-and-workload-diagnostics.md) |
| [TT-016](https://github.com/DanMarshall909/TurboTest/issues/6) | Exercise benchmark failure reports monitoring and cleanup | [Evidence](open/benchmarks/TT-016-exercise-benchmark-failure-reports-monitoring-and-cleanup.md) |
| [TT-017](https://github.com/DanMarshall909/TurboTest/issues/7) | Verify metadata command cancellation and process cleanup | [Evidence](open/benchmarks/TT-017-verify-metadata-command-cancellation-and-process-cleanup.md) |
| [TT-018](https://github.com/DanMarshall909/TurboTest/issues/8) | Verify executable signal cancellation and final cleanup | [Evidence](open/benchmarks/TT-018-verify-executable-signal-cancellation-and-final-cleanup.md) |
| [TT-019](https://github.com/DanMarshall909/TurboTest/issues/9) | Assert allocation policy and platform memory inputs | [Evidence](open/database/TT-019-assert-allocation-policy-and-platform-memory-inputs.md) |
| [TT-020](https://github.com/DanMarshall909/TurboTest/issues/10) | Assert default DI registration and observable options | [Evidence](open/api/TT-020-assert-default-di-registration-and-observable-options.md) |
| [TT-021](https://github.com/DanMarshall909/TurboTest/issues/11) | Close Docker adapter cancellation and permit gaps | [Evidence](resolved/database/TT-021-close-docker-adapter-cancellation-and-permit-gaps.md) |
| [TT-022](https://github.com/DanMarshall909/TurboTest/issues/12) | Assert lifecycle admission publication and drain races | [Evidence](open/api/TT-022-assert-lifecycle-admission-publication-and-drain-races.md) |
| [TT-023](https://github.com/DanMarshall909/TurboTest/issues/13) | Assert late failure ordering and safe diagnostic identity | [Evidence](resolved/api/TT-023-assert-late-failure-ordering-and-safe-diagnostic-identity.md) |
| [TT-024](https://github.com/DanMarshall909/TurboTest/issues/14) | Resolve retained-state cleanup and reentrancy invariants | [Evidence](resolved/api/TT-024-resolve-retained-state-cleanup-and-reentrancy-invariants.md) |
| [TT-025](https://github.com/DanMarshall909/TurboTest/issues/15) | Establish uncovered boundary reachability and limits | [Evidence](open/database/TT-025-establish-uncovered-boundary-reachability-and-limits.md) |
| [TT-026](https://github.com/DanMarshall909/TurboTest/issues/16) | Assert adaptive prepared and budget equality boundaries | [Evidence](open/database/TT-026-assert-adaptive-prepared-and-budget-equality-boundaries.md) |
| [TT-027](https://github.com/DanMarshall909/TurboTest/issues/17) | Assert atomic slot claims and shutdown transitions | [Evidence](open/database/TT-027-assert-atomic-slot-claims-and-shutdown-transitions.md) |
| [TT-028](https://github.com/DanMarshall909/TurboTest/issues/18) | Review prepared adapter diagnostic and observer coverage | [Evidence](open/database/TT-028-review-prepared-adapter-diagnostic-and-observer-coverage.md) |
| [TT-029](https://github.com/DanMarshall909/TurboTest/issues/19) | Make correctness verification reproducible and bounded in CI | Defined on GitHub |
| [TT-030](https://github.com/DanMarshall909/TurboTest/issues/20) | Verify final robustness across repeated lifecycles and failure recovery | Defined on GitHub |

[Migration mapping](evidence/2026-10-15/github-migration.json) retains stable issue and project identities.

[TT-000](resolved/database/TT-000-pool-feasibility-investigation.md) preserves the
completed feasibility investigation. Its prototypes are exploratory evidence,
not substitutes for the production acceptance gates above.

## Tracking and archival

Update live issue state, acceptance criteria and project fields on GitHub; do not
maintain competing status or priority fields in repository investigation records.
When an issue is resolved, link its evidence and record the investigation's
archive decision locally. Move a retained investigation from `open/<area>/` to
`resolved/<area>/` when that investigation is archived, preserving its TT identity
and GitHub URL. The folder is an evidence lifecycle, not a duplicate task board.
Completed TT-000 through TT-010 records remain unchanged.

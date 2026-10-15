# TurboTest tickets

Repository Markdown is the authoritative tracker. No GitHub remote or external
tracker is configured. This checkout uses local commits.

## Current status — 2026-10-15

All ten implementation tickets, TT-001 through TT-010, are Resolved. Independent
completion review passed for the final adaptive controller and performance work.
The table preserves implementation order and prerequisite identities.

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

The current candidate passes all **434 tests**, including **267 fast checks**,
without failures or skips. Solution and separate DynamicScalingExample builds
pass without warnings/errors. Final receipts bind identical compiler/test inputs;
independent completion review passed. Earlier ticket evidence retains
its original source cut rather than being relabelled as current verification.

The maintained Release experiments retain 16 fixed/parking cases and 2,544 cycles,
plus six fixed/adaptive cases and 954 cycles with equivalent startup conditions.
The [adaptive results and default rationale](evidence/2026-10-15/adaptive-pool-final/results.md)
retain fixed-mode wins, actual scale reasons, idle parking, resource observations
and limitations. The 50 ms aspiration concerns verified warm restoration of a
named workload; queueing, test body, startup and final drain are separate costs.
There is no universal latency threshold or optimal-window claim.

Whole-session mutation follows the final immutable committed snapshot and remains
pending at ticket publication. Its receipt is recorded in the final completion
record; no pending result is passing evidence.
The [post-session follow-up record](evidence/2026-10-15/post-session-mutation/follow-up.json)
retains the calibrated launch, completed EFCore execution, running benchmark
snapshot and explicit ownership of the pending report review. Local controllers
preserve results and clean owned layers; automatic semantic review and agent
wake-up are unavailable. A subsequent Codex follow-up must review both reports.
It is a separate follow-up to ordinary ticket completion and publication, with
fast-only mutation selection, calibrated slow-only exclusions, isolated writable
attempts and durable report handling required. Earlier scoped mutation evidence
is not proof of the final combined implementation.

[TT-000](resolved/database/TT-000-pool-feasibility-investigation.md) preserves the
completed feasibility investigation. Its prototypes are exploratory evidence,
not substitutes for the production acceptance gates above.

## Status changes

When implementation and verification earn resolution, update the ticket's
`status` to `Resolved`, add a dated decision-log entry, move it to
`resolved/<area>/`, and update dependent links. Keep the ticket identity. A linked
GitHub tracker should be used only if one is subsequently configured; do not
maintain competing authoritative statuses.

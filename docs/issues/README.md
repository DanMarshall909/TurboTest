# TurboTest tickets

Repository Markdown is the authoritative tracker. No GitHub remote or external tracker is configured. Ticket status and dependencies live in each ticket's frontmatter; this index provides navigation and the proposed implementation sequence.

Priority: **P0** repairs broken advertised behavior; **P1** establishes reliable pooling; **P2** adds adaptive management after those foundations.

## Implementation sequence

| Ticket | Work | Priority | Prerequisites |
| --- | --- | --- | --- |
| [TT-002](open/database/TT-002-reset-with-pooled-connections.md) | Reset safely with pooled PostgreSQL connections | P0 | None |
| [TT-001](open/api/TT-001-own-and-release-database-leases.md) | Own and release public database leases | P0 | TT-002 |
| [TT-003](open/database/TT-003-prepare-immutable-schema-and-seed-baselines.md) | Prepare immutable schema and seed baselines | P1 | TT-001, TT-002 |
| [TT-004](open/database/TT-004-restore-data-and-handle-schema-changes.md) | Restore data and handle schema changes | P1 | TT-003 |
| [TT-005](open/database/TT-005-bound-fixture-owned-prepared-database-pools.md) | Bound fixture-owned prepared database pools | P1 | TT-001, TT-003, TT-004 |
| [TT-006](open/database/TT-006-quarantine-recover-and-drain-pool-resources.md) | Quarantine, recover, cancel and drain resources | P1 | TT-005 |
| [TT-007](open/api/TT-007-use-the-configured-owner-in-di-and-static-apis.md) | Fix DI and static lifecycle ownership | P1 | TT-005, TT-006 |
| [TT-008](open/database/TT-008-park-and-rewarm-prepared-databases.md) | Park and rewarm prepared databases | P2 | TT-005, TT-006 |
| [TT-010](open/database/TT-010-measure-pool-latency-and-correct-performance-claims.md) | Measure latency and correct performance claims | P1 | TT-005, TT-006 for integrated benchmarks |
| [TT-009](open/database/TT-009-adapt-warm-capacity-to-sustained-demand.md) | Adapt warm capacity to sustained demand | P2 | TT-008, TT-010 |

TT-007 and TT-008 can proceed independently once their prerequisites are complete. TT-010's metrics and fixed-pool benchmark land before the adaptive controller; its adaptive comparison completes alongside TT-009. The README claim correction in TT-010 can be made immediately using the existing evidence.

## Investigation and evidence

[TT-000: completed pool feasibility investigation](resolved/database/TT-000-pool-feasibility-investigation.md) records the source revision, observed bugs, prototype measurements, caveats and retained evidence. It is a completed experiment, not completion of any implementation ticket above.

All ten implementation tickets are Open. Their acceptance criteria describe future work; the prototypes do not satisfy the production acceptance gates. The 50 ms goal applies to a defined warm operation and workload, with queueing and full lifecycle costs reported separately.

## Status changes

When a ticket is implemented and verified, update its `status` to `Resolved`, add a dated decision-log entry with verification evidence, move it to `resolved/<area>/`, and update links in this index and dependent tickets. Keep its ticket ID. Use a linked GitHub issue only if an external tracker is subsequently configured; reconcile ownership rather than maintaining competing statuses.

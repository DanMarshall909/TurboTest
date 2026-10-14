# TurboTest tickets

Repository Markdown is the authoritative tracker. No GitHub remote or external tracker is configured. Ticket status and dependencies live in each ticket's frontmatter; this index provides navigation and the proposed implementation sequence.

Priority: **P0** repairs broken advertised behavior; **P1** establishes reliable pooling; **P2** adds adaptive management after those foundations.

## Implementation sequence

| Ticket | Work | Priority | Prerequisites |
| --- | --- | --- | --- |
| [TT-002](resolved/database/TT-002-reset-with-pooled-connections.md) | Reset safely with pooled PostgreSQL connections | P0 | None |
| [TT-001](resolved/api/TT-001-own-and-release-database-leases.md) | Own and release public database leases | P0 | TT-002 |
| [TT-003](resolved/database/TT-003-prepare-immutable-schema-and-seed-baselines.md) | Prepare immutable schema and seed baselines | P1 | TT-001, TT-002 |
| [TT-004](resolved/database/TT-004-restore-data-and-handle-schema-changes.md) | Restore data and handle schema changes | P1 | TT-003 |
| [TT-005](resolved/database/TT-005-bound-fixture-owned-prepared-database-pools.md) | Bound fixture-owned prepared database pools | P1 | TT-001, TT-003, TT-004 |
| [TT-006](resolved/database/TT-006-quarantine-recover-and-drain-pool-resources.md) | Quarantine, recover, cancel and drain resources | P1 | TT-005 |
| [TT-007](open/api/TT-007-use-the-configured-owner-in-di-and-static-apis.md) | Fix DI and static lifecycle ownership | P1 | TT-005, TT-006 |
| [TT-008](open/database/TT-008-park-and-rewarm-prepared-databases.md) | Park and rewarm prepared databases | P2 | TT-005, TT-006 |
| [TT-010](open/database/TT-010-measure-pool-latency-and-correct-performance-claims.md) | Measure latency and correct performance claims | P1 | TT-005, TT-006 for integrated benchmarks |
| [TT-009](open/database/TT-009-adapt-warm-capacity-to-sustained-demand.md) | Adapt warm capacity to sustained demand | P2 | TT-008, TT-010 |

TT-007 and TT-008 can proceed independently once their prerequisites are complete. TT-010's metrics and fixed-pool benchmark land before the adaptive controller; its adaptive comparison completes alongside TT-009. The README claim correction in TT-010 can be made immediately using the existing evidence.

## Investigation and evidence

[TT-000: completed pool feasibility investigation](resolved/database/TT-000-pool-feasibility-investigation.md) records the source revision, observed bugs, prototype measurements, caveats and retained evidence. It is a completed experiment, not completion of any implementation ticket above.

TT-001 through TT-006 are Resolved. The remaining four implementation tickets are Open. Their acceptance criteria describe future work; the prototypes do not satisfy the production acceptance gates. The 50 ms goal applies to a defined warm operation and workload, with queueing and full lifecycle costs reported separately.

## Implementation progress

The P0 candidate implements explicit public leases and pooled PostgreSQL reset.
Factory and static callers and all maintained examples use `CreateLeaseAsync`
and `lease.Context`; source version `1.0.0-alpha.2` removes the raw-context APIs
and implicit conversion. Lease return closes contexts/readers/transactions before
reset and retains one completion result. Ordinary row reset preserves database
and session identity and leaves failures unavailable for reuse.

The full suite passes all 38 checks without skips. Ten fast ownership checks
execute the lease, return and diagnostic classes at 100% line and branch
coverage. Stryker rejects all 28 executable mutants in those classes, with ten
source-resolvable participating tests and no survivors, timeouts or uncovered
mutants. This is scoped evidence, not whole-package coverage or mutation proof.
The [current evidence manifest](evidence/2026-10-14/public-leases-repair/manifest.json)
retains RED/GREEN results, deliberate missing/double-return faults, coverage,
mutation reports, source snapshots and the earlier failed full-suite runs.

Startup investigation reproduced failures while empty clusters were still in
`initdb`. Disposable cluster creation now skips its initial disk flush while
retaining server `fsync`; a separate query-failure regression protects readiness
connection cleanup. The passing full suite left no TurboTest containers.
[Earlier reset evidence](evidence/2026-10-14/pooled-reset/manifest.json) and
[internal protocol evidence](evidence/2026-10-14/ownership-protocol/manifest.json)
retain their original snapshots and limitations.

Independent completion review passed for TT-001 and TT-002; both are Resolved.

The later tickets track restoration, capacity, recovery, DI ownership, parking
and adaptation in their dependency order.

TT-010's unsupported README/provider/package latency claims have been removed.
Maintained metrics and integrated benchmarks remain open.

## Status changes

When a ticket is implemented and verified, update its `status` to `Resolved`, add a dated decision-log entry with verification evidence, move it to `resolved/<area>/`, and update links in this index and dependent tickets. Keep its ticket ID. Use a linked GitHub issue only if an external tracker is subsequently configured; reconcile ownership rather than maintaining competing statuses.

TT-003 implements immutable prepared schema and seed baselines.
Its latest full suite passes 108 checks without failures or skips; seed,
migration/history, sequence, template isolation and failed-startup ownership
results are retained in the
[progress evidence](evidence/2026-10-14/prepared-baseline-progress/manifest.json).
The [repair evidence](evidence/2026-10-14/prepared-baseline-repair/manifest.json)
records cleanup retry, workload verification, catalog/dependency, configuration
and context-model repairs. Solution and separate example builds pass.
Independent completion review passed; TT-003 is Resolved. That review left seven
open tickets; the subsequent TT-004 progress is recorded below.

TT-004 implements verified row restoration and schema-change recreation. It restores
updated/deleted seeds, relationships and sequences in the same database; schema
changes, cycles and oversized seed caches use verified recreation. Named reset
faults are rejected and timing separates permit wait, execution and validation.
The local 130/1,300/13,000-row probe reports reset p95 62.83/102.48/305.74 ms,
including validation; these are scoped observations, not a 50 ms guarantee.
The first 128-test full gate has 117 passes and 11 failures, mostly startup
timeouts under default collection parallelism. The bounded normal-runner rerun
passes all 128 tests with no failures or skips; solution and separate example
builds pass. [Progress evidence](evidence/2026-10-14/row-reset-progress/manifest.json)
retains raw results and source/calibration snapshots.
Independent completion review passed; TT-004 is Resolved. Capacity, recovery, DI,
parking, comprehensive metrics and adaptation remain in six open tickets.

TT-005 implements fixed ordinary/prepared
reservation caps, cancelable capacity waiting, captured resource budgets,
independent preparation/reset limits and public pool state. The final full gate
passes 168 checks without failures or skips; solution and separate example builds
pass. Lost-create response cleanup and private ambient-transaction isolation have
native regressions. [Final candidate evidence](evidence/2026-10-14/bounded-pool-final/manifest.json)
retains current source, raw results, earlier failed attempts and coverage limits.
Independent completion review passed; TT-005 is Resolved. Recovery, DI, parking,
comprehensive metrics and adaptation remain in five open tickets.
The [earlier progress manifest](evidence/2026-10-14/bounded-pool-progress/manifest.json)
remains an unchanged historical public-state checkpoint.

TT-006 implements bounded verified recovery, startup cancellation and owned
drain. Uncertain resources retain capacity, terminal exhaustion settles waiters,
and caller deadlines preserve actual jobs through cleanup capabilities. Blocking
and throwing callbacks remain tracked; failed prepared workload cleanup retains
its host. The whole-suite gate passes212 checks without failures/skips. The subsequent
unused-getter pruning passes103 fast/native checks; final solution and separate
example builds pass. [Final evidence](evidence/2026-10-14/resource-recovery-final/manifest.json)
retains raw results, source cuts and earlier failed attempts. Independent
completion review passed; TT-006 is Resolved. DI, parking, metrics and adaptation
remain in four Open tickets. The [earlier progress archive](evidence/2026-10-14/resource-recovery-progress/manifest.json)
remains an unchanged historical90-check checkpoint.

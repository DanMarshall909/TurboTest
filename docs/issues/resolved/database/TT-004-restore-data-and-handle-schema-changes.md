---
id: TT-004
title: Restore data and handle schema changes
status: Resolved
priority: P1
type: Feature
created: 2026-10-14
depends_on: [TT-003]
---

# TT-004: Restore data and handle schema changes

## Problem

Prepared databases need a fast data reset that restores the baseline, including seed rows changed/deleted by tests. The prototype's child-first DELETE plus reseeding was faster than TRUNCATE on the measured small/medium fixture. Schema-changing workloads need a separate recreation path.

Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md). Observed sequential reset-to-verified-ready DELETE p95 was 17.67/18.57/64.19 ms at 130/1,300/13,000 inserted rows. These are workload observations, not defaults or guarantees.

## Scope

Build/cache a foreign-key-aware cleanup plan for each baseline, restore supported seed/sequence state, and use baseline recreation when row cleanup cannot safely satisfy the contract. Choose/document supported schema-change detection or explicit schema-changing mode; the prototype's public-schema fingerprint is incomplete.

## Acceptance criteria

- [x] Updating/deleting seed rows, inserting parent/child rows and advancing sequences is followed by exact baseline restoration, including next generated IDs and relationships.
- [x] The cached plan handles supported foreign-key ordering; cycles, triggers and unsupported objects produce a documented safe strategy or explicit fallback, never silent partial cleanup.
- [x] A changed schema cannot pass as an ordinary clean database. At minimum, tests adding a table/column demonstrate baseline recreation before reuse.
- [x] The supported catalog scope is explicit, including the behavior for views, routines, types, grants, extensions and non-public schemas.
- [x] Reset success is reported only after required baseline validation. Errors, including a failure after `setval`, cannot publish a dirty database; sequence changes cannot be assumed to roll back with row changes.
- [x] Performance measurements include restoration and verification, and account for executing cleanup plus time waiting for a reset permit.

## Verification

Reject faults in row cleanup, reseeding, sequence restoration and schema detection. Keep real PostgreSQL checks for sequence rollback behavior and schema recreation. Do not hardcode the three-table experiment's SQL as a general reset implementation.

## Implementation design

Cache one fixture-owned row-reset plan before sealing the template. Read ordinary
tables and foreign-key dependencies from every supported user namespace; delete
children before parents and restore parents before children. Cache PostgreSQL
raw binary COPY streams rather than decoding arbitrary built-in column types.
Generated columns are recomputed by COPY; explicit identity values are restored.
Capture every permanent sequence's `last_value` and `is_called`, independently
of row maxima. Clear only the slot's source before reset so cached sequence
allocations cannot escape restoration.

The row path applies only to an unchanged supported catalog. Check the complete
catalog before cleanup and validate the complete baseline after restoration.
Cycles or a seed cache exceeding its bounded memory budget use template
recreation. Schema additions/changes and newly unsupported objects also use
recreation. The accepted TT-003 initialization support boundary remains explicit;
this ticket does not silently admit views, routines, custom types or extensions.
Sequence restoration is not rollback-safe: any reset/validation failure keeps
the slot unavailable and discards it before another lease can use it.

References: [PostgreSQL COPY](https://www.postgresql.org/docs/16/sql-copy.html),
[Npgsql raw binary COPY](https://www.npgsql.org/doc/copy.html),
[sequence rollback behavior](https://www.postgresql.org/docs/16/functions-sequence.html).

## Implementation progress

- **2026-10-14 — next dependency-ready ticket:** TT-003 is independently accepted
  at `07d0ed574598a38464b4f64ccab7e28b5c482c7e`. The first regression will update
  a parent seed, delete a child/note seed, insert a new related chain and advance
  sequences, then require exact restoration in the same physical database.
  Existing baseline tests protect initialization, migration/history and clone
  validation; this boundary check uniquely proves in-place restoration.
  Seven tickets remain Open; no TT-004 completion is claimed.
- The first RED observes database OID 16423 becoming 16424 despite correct seed
  restoration. Cached binary seed streams and dependency-ordered DELETE/COPY now
  preserve the database. The row regression, standalone `CACHE 5` sequences and
  migration-only schema/history checks pass. Coverage executes the restore body
  at 100% line/branch; cycle/cache-budget and error paths remain separate work.
  Production refactoring separates schema checks and workload readiness from
  return ownership, and caches the immutable schema signature once. The existing
  independent seed/ID oracle needs no structural refactor.
- Seven added table/column/namespace/view/routine/type/grant scenarios prove
  whole-template recreation. Removing the schema precheck makes all seven fail;
  restoring it makes all seven pass, with no task containers left. This calibrates
  preservation of TT-003's recreation semantics after introducing the row path.
- A bounded catalog investigation found that added operators/conversions and
  current-database comments can escape the prior fingerprint. TT-004's complete
  schema-change requirement owns rejection/capture of these categories; focused
  recreation oracles precede the repair. No completion is claimed.
- All three operator/conversion/shared-comment REDs observed the unchanged
  database OID; their guarded recreation checks now pass. Three further real
  REDs reproduce changed `IS_TEMPLATE`, table-row-type casts and type ACLs.
  Capturing autogenerated type definitions/ACLs and rejecting casts/nondefault
  template flags closes those gaps. PostgreSQL refuses DROP on a template-marked
  slot; clearing that owned slot's flag before deletion repairs the first GREEN
  attempt. All six restored checks pass. Shared comments and these unsupported
  categories are also rejected at initial capture.
- Cycle and 17 MiB seed fixtures safely recreate. The cache retains at most
  16 MiB of binary streams including framing (temporary copying can use more
  memory); a 17 MiB fixture is independently checked by byte length and a .NET
  SHA-256 oracle after recreation. The cross-schema fixture restores bytea,
  arrays, JSON, an explicit ALWAYS identity and a generated column in place;
  actual next identity 50 repeats after clearing a `CACHE 5` source. All three
  checks pass. An earlier JSON literal hit EF raw-SQL formatting before
  initialization; that setup failure is retained and is not behavior RED.
- Five one-at-a-time faults omit deletion, binary reseeding, sequence restoration,
  cycle fallback or cache-budget fallback. Each fails its independent boundary
  regression and restores the exact source hash. Removing full post-commit
  validation also fails: the dirty return reports success instead of the required
  `BaselineRowReset` failure. Restored checks pass; a real rollback observation
  confirms `setval` remains visible after row rollback. The failed slot is deleted,
  a subsequent public lease receives a different verified database, and repeated
  failed return shares its result.
- Reset timing RED observes zero records where two completed returns require
  separate measurements. Event 400 / `BaselineReset` now reports permit wait,
  executing reset (including restoration), validation, strategy and outcome with
  an opaque owner ID and no seed payloads. A held first reset queues a second;
  its wait measurement grows independently of execution. The timing law and two
  failure checks pass. The current permit is the shared lifecycle gate; TT-005
  will separate preparation/reset bounds. Timing excludes diagnostic emission.
- A bounded one-off local performance probe will measure 130/1,300/13,000 inserted
  rows with a 10:2:1 parent/child/note mix, mutated/deleted seeds, actual database
  reuse, startup and final drain. Six counterbalanced samples per size retain
  public reset/cycle and separated permit/execution/validation times. Incorrect
  zero-row/distribution runs must be rejected before timing. This is TT-004 local
  evidence, not TT-010's maintained benchmark or a portable 50 ms guarantee.
- The probe rejects both all-zero and 100/20/0 distributions before timing.
  Its valid run completes 18 counterbalanced samples, independently verifies
  restored seed tuples/IDs and generated IDs 101/201/301, and retains the same
  database. Public reset p95 is 62.83/102.48/305.74 ms at the three loads;
  validation p95 is 45.32/50.87/46.98 ms and is included. Startup is 4.33 s,
  final drain 1.49 s. The results exceed the 50 ms aspiration; no guarantee is
  inferred from this small shared-host experiment.
- The first full gate encounters startup timeouts across preexisting and new
  test classes. Host I/O pressure reaches roughly 28% over its 60-second window,
  with 24 available CPU threads determining xUnit's default collection parallelism.
  A sanitized live startup diagnostic is retained; already-deleted failed
  containers cannot provide retrospective logs, so the exact timeout phase is
  not established. The normal runner now bounds concurrent test collections to
  two using the conservative scheduler; explicit within-test concurrency cases
  and all assertions remain active. No startup timeout is increased or failure
  skipped. Full verification under this resource budget remains pending.
  See [xUnit runner configuration](https://xunit.net/docs/config-xunit-runner-json).

## Final candidate verification

- **2026-10-14 — candidate for independent review:** The normal full gate using
  the deployed two-collection resource budget passes **128 tests**, with zero
  failures or skips, in 8 m 20 s. Explicit concurrent lease/fixture assertions
  remain exercised. Solution and separate DynamicScalingExample builds pass
  with zero warnings/errors. No TurboTest containers remain; the unrelated
  container is preserved.
- Full coverage executes the row-plan capture/restore bodies, row-reset checks
  and owned-slot deletion at 100% line/branch. Catalog safety-size guards and
  some return/shutdown/failed-compensation paths remain uncovered; no
  whole-package coverage or mutation claim is made. These gaps are visible in
  the retained full report.
- [Progress evidence](../../evidence/2026-10-14/row-reset-progress/manifest.json)
  retains raw RED/GREEN/fault results, coverage, source snapshots, the rejected
  broad gate, runner deployment/resource evidence and the bounded local probe.
  Optional internal fault seams preserve the public API's production defaults.
  Historical calibration snapshots are distinct from later timing/failure-hook
  source changes; normal mutation remains pending the whole coding session's
  final source commit and will exclude end-to-end tests.
- Refactoring separates immutable schema signatures, seed-stream capture,
  dependency-ordered restoration, validation and return ownership. Tests retain
  independent tuples/IDs and object-absence oracles; shared catalog-case setup
  avoids duplicating their lifecycle logic. The duplicated seed query in the
  failure check is removed. Real raw COPY, cached sequences and rollback behavior
  require PostgreSQL boundary evidence; existing fast ownership/manifest checks
  remain the cheap general feedback lane.
- TT-004 remains **Open** until independent completion review passes and the
  exact completion envelope is committed. Six later tickets remain required
  after this candidate; no broader goal completion is claimed.

## Decision log

- **2026-10-14 — Resolved:** Independent completion review passed for generic
  seed/sequence restoration and schema-change recreation. The final full suite
  passes 128 tests without failures or skips; solution and separate example
  builds pass. [Completion receipt](../../evidence/2026-10-14/row-reset-progress/completion.json)
  identifies the exact reviewed candidate. Historical failures and scoped local
  latency measurements remain retained. Capacity limits, broader recovery, DI,
  parking, comprehensive metrics and adaptation remain in six open tickets.

---
id: TT-002
title: Reset safely with pooled PostgreSQL connections
status: Resolved
priority: P0
type: Bug
created: 2026-10-14
depends_on: []
---

# TT-002: Reset safely with pooled PostgreSQL connections

## Problem

The first factory reset with ordinary Npgsql pooling failed with PostgreSQL SQLSTATE `55006` because physical sessions remained attached to the source database. The current release path destroys the container after reset failure. Disabling pooling allowed reuse, but forfeits the connection warmup benefit.

Source: [TtContainer.cs](../../../../src/TurboTest.EFCore/Internal/TtContainer.cs), [TtPool.cs](../../../../src/TurboTest.EFCore/Internal/TtPool.cs), [TtDatabase.cs](../../../../src/TurboTest.EFCore/Internal/TtDatabase.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Fix source-database administrative operations while preserving pooling. Own the affected data sources, dispose lease resources, and ensure the source has no incompatible sessions before clone/drop operations. Ordinary row cleanup should retain its pool. Scope clearing and termination to resources owned by this fixture.

## Acceptance criteria

- [x] A warmed, pooled database can complete its first and subsequent resets and be reacquired without `55006`, container replacement or a requirement to set `Pooling=false`.
- [x] Connections to a clone/template source are closed or drained before administrative operations require exclusivity; active lease readers/transactions cannot be silently reused by another test.
- [x] Clearing a fixture's pool cannot clear another fixture's data source or terminate unrelated database sessions. Avoid process-wide `ClearAllPools` in normal operation.
- [x] Timeout or failure produces a diagnostic with the failed lifecycle stage and leaves the resource unavailable for reuse; connection strings/passwords are not logged.
- [x] A real PostgreSQL regression checks resource identity before/after reset and verifies removal of ordinary test rows.

## Verification

Exercise idle pooled sessions and an outstanding reader/transaction. Assert the chosen rejection/drain behavior, not an arbitrary sleep. Full prepared seed restoration belongs to TT-003/TT-004; capacity replenishment after failure belongs to TT-006.

## Implementation log

- **2026-10-14 — in progress:** Replaced destructive empty-template reset with
  transactional TRUNCATE of ordinary user tables, retaining the slot's owned
  Npgsql pool. All context paths use that source. Factory return closes its EF
  resources before reset; failed callbacks discard the resource and report
  `LeaseCleanup`. Reset errors report `ResetConnect`, `ResetRows` or
  `ResetCommit` plus SQLSTATE/type, without SQL, payloads or credentials. A five
  second lock timeout rejects an incompatible outstanding transaction rather
  than publishing dirty state. No clone/drop administrative operation remains
  in ordinary reset. This is empty-data cleanup, not immutable seed/schema
  baseline restoration; TT-003/TT-004 still own that contract.
- Eight PostgreSQL checks pass with .NET SDK 10.0.301 and the cached PostgreSQL
  16 image. The original reset regression failed on changed database identity;
  fault probes separately failed on swallowed reset and cleanup errors.
  [Initial evidence](../../evidence/2026-10-14/pooled-reset/manifest.json)
  includes current source hashes, logs, TRX and coverage. Reset and callback
  disposal executed at 100% line coverage in this targeted scope; missing
  Docker-container handling remains uncovered. Coverage is not mutation proof.
  The tests intentionally belong to the slower PostgreSQL boundary lane.
- Added a real regression for user schema `pgfixture`: the original `LIKE
  'pg_%'` filter left its row behind because `_` matched any character. The
  filter now excludes the literal reserved `pg_` prefix. This checks ordinary
  non-public table cleanup; general catalog/baseline restoration remains TT-004.
- [Current evidence](../../evidence/2026-10-14/ownership-protocol/manifest.json)
  records a passing 13-check combined run (eight PostgreSQL, five fast ownership
  checks). Calibrated missing-return, double-return and lost-original-diagnostic
  faults are rejected. Stryker 5.0.0 rejects all 16 executable mutants in
  `TtDatabase`/`TtLifecycleException`; five catalogued tests have source-resolvable
  mutant links, with zero survivors, timeouts, uncovered mutants or unlisted
  links. This is mutation evidence for those classes, not the entire package.
- Production refactoring separates resource ownership from Docker actions via
  return/discard delegates and retains one disposal completion task. Test
  refactoring provides fast ownership laws while retaining PostgreSQL identity,
  reader, transaction and failure-boundary checks. A Stryker constructor-pattern
  injection crash was repaired by an equivalent explicit cast, preserving all
  configured mutation scope; failed runs are retained.
- Completion remains unclaimed: independent review and a passing broad gate
  are outstanding. The archived evidence above precedes the public lease
  migration. Current PostgreSQL regressions return individual public leases
  before reacquisition; TT-001 now implements the ownership API and is awaiting
  its fresh completion gates. No prepared-baseline claim is made.

- **2026-10-14 — candidate verification:** The full suite passes all 33 checks
  without skips; the solution and the separate dynamic-scaling example compile
  without warnings. Eight fast ownership checks execute the lease/return/safe
  diagnostic classes at 100% line and branch coverage. Fresh Stryker 5.0.0 rejects
  all 26 executable scoped mutants; eight catalogued tests have source-resolvable
  links, with no survivors, timeouts, uncovered mutants or unlisted links.
  Missing-return and double-return faults fail named assertions. The
  [public lease evidence](../../evidence/2026-10-14/public-leases/manifest.json)
  retains earlier failures as well as the final pass. Independent completion
  review is pending; status remains Open.

- **2026-10-14 — independent review repair:** The first review returned FAIL:
  secondary discard/deletion errors could replace the original safe diagnostic,
  and the minimal static example skipped final shutdown after exceptions. Named
  fast and PostgreSQL REDs reproduce all three compensation paths. Diagnostics
  now retain the first stage/SQLSTATE and report only safe secondary metadata;
  unconfirmed resources remain owned and unavailable. A deliberate premature
  ownership-removal fault is rejected by the retained-container regression.
  The minimal example now owns an async-disposable factory. The same injected
  runtime exception left one container before repair and zero afterward.
  The first fault-injection compile failure is retained and is not counted as RED.
  The repaired full suite passes all 38 checks without skips. Ten fast checks
  execute the ownership/diagnostic classes at 100% line and branch coverage.
  Fresh Stryker rejects all 28 executable scoped mutants with ten source-linked
  participants and no survivors/timeouts/uncovered mutants. Re-review is pending;
  both tickets remain Open. See the
  [repair evidence](../../evidence/2026-10-14/public-leases-repair/manifest.json). The existing
  stash at `0811caaa6d33c0107d31416a68b3dc30a8a218ec` remains untouched.

## Decision log

- **2026-10-14 — Resolved:** Independent completion review passed for the P0
  lease/reset candidate. All 38 full-suite checks pass without skips; solution
  and example builds pass. Ten fast ownership checks and 28 rejected executable
  scoped mutants protect the return protocol. The
  [completion receipt](../../evidence/2026-10-14/public-leases-repair/completion.json)
  identifies the reviewed candidate. Earlier in-progress entries remain as
  history. Prepared baselines, capacity bounds, recovery, DI ownership, parking,
  metrics and adaptation remain in their separate open tickets.

---
id: TT-002
title: Reset safely with pooled PostgreSQL connections
status: Open
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

- [ ] A warmed, pooled database can complete its first and subsequent resets and be reacquired without `55006`, container replacement or a requirement to set `Pooling=false`.
- [ ] Connections to a clone/template source are closed or drained before administrative operations require exclusivity; active lease readers/transactions cannot be silently reused by another test.
- [ ] Clearing a fixture's pool cannot clear another fixture's data source or terminate unrelated database sessions. Avoid process-wide `ClearAllPools` in normal operation.
- [ ] Timeout or failure produces a diagnostic with the failed lifecycle stage and leaves the resource unavailable for reuse; connection strings/passwords are not logged.
- [ ] A real PostgreSQL regression checks resource identity before/after reset and verifies removal of ordinary test rows.

## Verification

Exercise idle pooled sessions and an outstanding reader/transaction. Assert the chosen rejection/drain behavior, not an arbitrary sleep. Full prepared seed restoration belongs to TT-003/TT-004; capacity replenishment after failure belongs to TT-006.

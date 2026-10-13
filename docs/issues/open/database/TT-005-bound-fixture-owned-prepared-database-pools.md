---
id: TT-005
title: Bound fixture-owned prepared database pools
status: Open
priority: P1
type: Feature
created: 2026-10-14
depends_on: [TT-001, TT-003, TT-004]
---

# TT-005: Bound fixture-owned prepared database pools

## Problem

The existing allocator can create overflow containers as acquisition pressure increases. Tests need exclusive prepared databases with bounded resource use and explicit queueing. Warming pooled connections alone does not produce available clean databases.

Source: [TtPool.cs](../../../../src/TurboTest.EFCore/Internal/TtPool.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Introduce a fixture-owned pool of prepared database slots with owned Npgsql data sources. Support several databases in a container where appropriate. Define separate limits for containers, physical database reservations, connections and executing resets; clarify how existing `PoolSize` configuration maps to them.

## Acceptance criteria

- [ ] Each test lease owns exactly one database; concurrent leases cannot obtain the same slot. Add a cross-database marker test that explicitly checks markers are absent from other leased databases.
- [ ] Preparing, Ready, Leased, Resetting, Quarantined, Parked and Retiring reservations count against the physical cap whenever those states exist. Reserve before creation; release only after confirmed cleanup/deletion.
- [ ] At capacity, acquisition waits with documented cancellation/timeout behavior. It cannot create an unbounded overflow container or bypass the cap during concurrent requests.
- [ ] Only a verified Ready slot is handed out. Return/reset/validation can run in the background, but readiness and throughput cannot be reported before that work completes.
- [ ] Preparation and executing cleanup have separate concurrency bounds; waiting resets are distinguished from actual executing resets.
- [ ] Per-database connection limits and an administrator/bootstrap allowance form an explicit connection budget. One fixture cannot reuse another fixture's baseline/data source.
- [ ] A fixed-capacity mode is usable and observable before adaptive decisions are introduced.

## Verification

Test cap enforcement before async creation completes, concurrent exclusive leases, fifth-request backpressure on four slots, release only after validation, and fixture isolation. Exercise the public API against real PostgreSQL; pure ownership/controller tests provide fast feedback.

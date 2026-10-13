---
id: TT-007
title: Use the configured owner in DI and static APIs
status: Open
priority: P1
type: Bug
created: 2026-10-14
depends_on: [TT-005, TT-006]
---

# TT-007: Use the configured owner in DI and static APIs

## Problem

`AddTurboTestDbContext` calls `GetConnectionString`, which always throws. `TtServiceFactory` initializes its own pool then delegates context/factory acquisition to static `Tt`, creating a second pool and bypassing DI configuration. Static disposal also leaves initialization state pointing at the disposed pool.

Source: [service registration](../../../../src/TurboTest.EFCore/Extensions/ServiceCollectionExtensions.cs), [TtServiceFactory.cs](../../../../src/TurboTest.EFCore/TtServiceFactory.cs), [Tt.cs](../../../../src/TurboTest.EFCore/Tt.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Route DI and static conveniences through the supported lease/fixture owner. Define synchronous DI resolution versus async fixture initialization explicitly; remove or replace the unusable connection-string contract. Preserve a clear lifetime relationship between service scope, context lease and fixture.

## Acceptance criteria

- [ ] The documented DI context registration resolves and executes a query; it does not call a method that unconditionally throws.
- [ ] DI context/factory paths use exactly the configured owner, not a separately initialized pool plus the global pool. Limits, schema/seed initializer, image, Docker host and logging configuration are honored.
- [ ] Leased DI registrations document `CreateAsyncScope`/`DisposeAsync`. Async scope disposal awaits lease return; async provider disposal awaits owned-resource drain under TT-006. Neither reports completion while cleanup is pending.
- [ ] Synchronous scope/provider disposal has an explicit tested contract: reject async-only cleanup with an actionable use-async-disposal diagnostic, or implement a documented supported synchronous path. It cannot silently leak a lease or conceal sync-over-async blocking.
- [ ] Two independent providers with different options/baselines do not share mutable fixture state or affect each other's disposal.
- [ ] Static create → dispose → create either constructs a new valid owner or rejects reuse with a documented lifecycle error; it never accesses a disposed pool.
- [ ] Concurrent initialization/disposal has one owner and no orphan pool. Configuration changes after initialization have a documented validated policy.
- [ ] Examples describe supported initialization and lifetime usage, with no hidden sync-over-async requirement for ordinary async use.

## Verification

Use service-provider integration tests that check database identity, applied options and task-owned resource counts. Include direct/factory/DI parity and reinitialization regressions.

# TurboTest 🚀

Containerized PostgreSQL testing for Entity Framework Core.

## Quick Start

```csharp
await using var fixture = await Tt.CreateFactoryAsync<MyDbContext>(o => o.PoolSize = 1);
await using var lease = await fixture.CreateLeaseAsync();
var context = lease.Context;

// Use your context normally
context.Users.Add(new User { Email = "test@example.com" });
await context.SaveChangesAsync();
```

## Installation

The alpha.2 API is available from this source checkout. Add a project reference
from your test project, adjusting the path for your checkout:

```bash
dotnet add reference ../TurboTest/src/TurboTest.EFCore/TurboTest.EFCore.csproj
```

## Why TurboTest?

- ⚡ **Reusable**: Work toward prepared database reuse; see the [pooling backlog](docs/issues/README.md)
- 🎯 **Simple**: One-line setup, no Docker knowledge required
- 🔒 **Real**: Actual database with constraints, triggers, and indexes
- 🧪 **Accurate**: Catch issues that in-memory databases miss

## Features

✅ Foreign key constraint validation  
✅ Unique constraint enforcement  
✅ Transaction isolation testing  
✅ Database-specific SQL syntax  
✅ Stored procedures and triggers  
✅ Connection pooling behavior  

## Example

```csharp
public class BlogTests
{
    [Fact]
    public async Task Should_Enforce_Foreign_Keys()
    {
        // This test would incorrectly pass with in-memory database!
        await using var fixture = await Tt.CreateFactoryAsync<BlogContext>();
        await using var lease = await fixture.CreateLeaseAsync();
        var context = lease.Context;
        
        // Try to add post without a blog (violates FK)
        context.Posts.Add(new Post 
        { 
            Title = "Test",
            BlogId = 999 // Non-existent blog
        });
        
        // Real database throws FK violation
        await Assert.ThrowsAsync<DbUpdateException>(() => 
            context.SaveChangesAsync());
    }
}
```

## Supported Databases

- PostgreSQL — currently the only implemented provider

The 50 ms aspiration covers warm row cleanup, seed restoration and validation
for a defined workload. It excludes startup, checkout queueing, the test body and
final shutdown. Local three-table prototype measurements met that target for
some 130/1,300-row cases and exceeded it at 13,000 rows. These are experimental
observations, not a production latency guarantee; see the
[investigation and raw evidence](docs/issues/resolved/database/TT-000-pool-feasibility-investigation.md).

## Lease lifetime and alpha.2 migration

The `1.0.0-alpha.2` source removes raw `CreateContextAsync` methods and implicit
context conversion. Replace `await using var context = await ...CreateContextAsync()`
with `await using var lease = await ...CreateLeaseAsync(); var context = lease.Context;`.
This is an explicit breaking prerelease change: a raw context cannot retain
ownership of database return. No published NuGet package or release tags were
found when making this decision.

Each lease owns one test database. Use `lease.CreateContext()` for another
context in the same test. You can dispose an individual context synchronously or
asynchronously; the lease still owns the database. Always asynchronously dispose
the lease to close all remaining contexts, readers and transactions and await
reset. Concurrent/repeated lease disposal waits for the same result, including
failures. A disposed lease rejects context access and additional contexts.

Factory disposal closes outstanding leases and deletes its owned resources.
Construction or initialization failure closes acquired resources and discards
the database before reporting a stage diagnostic. Cancellation before lease
publication follows the same cleanup path; cancellation after publication leaves
cleanup with the caller. Caller cancellation cannot interrupt lease return.
If compensating cleanup also fails, the lifecycle diagnostic retains the original
stage/SQLSTATE and reports the secondary failure without its payload. Failed
cancellation cleanup reports this lifecycle failure; successful cleanup preserves
the cancellation result. Unconfirmed resources remain owned for final cleanup.
`StartupTimeout` bounds the caller's initialization wait (default 30 seconds).
Caller cancellation or expiry reports `StartupCanceled` or `StartupTimeout`
with a cleanup capability. Cooperative Docker/readiness work receives the
token; any pending noncooperative work remains owned until it actually settles.
An issued container create stays awaited until its response records the resource
identity, because canceling an HTTP request cannot undo Docker's creation.
Cleanup starts in the background and waits for initialization before deleting
resources. A canceled queued acquisition removes only that request; preparation
continues under its owner and can serve another caller.

`DrainTimeout` bounds the factory caller's drain wait (default 30 seconds).
Shutdown rejects new acquisitions, cancels queued requests, and awaits active
acquisition, context cleanup, return and lifecycle jobs before deleting resources.
A timeout reports `DrainTimeout` with `TtLifecycleException.Cleanup`; it leaves
actual work running under that owner. Repeated disposal shares its original
result. Await `error.Cleanup.RetryCleanupAsync()` to join unfinished work or retry
completed failed cleanup; retries share one pending attempt. Explicit retry waits
for actual completion and has no implicit deadline. Global `Tt.DisposeAsync()`
also retries retained owners, bounding each retained observation by the configured
drain timeout while allowing other owners to progress. A failed prepared workload
drop retains its host until workload cleanup succeeds.
Cancellation callbacks are tracked asynchronously and cannot block the caller's
deadline observation. Callback failures after a timeout are reported through the
retained cleanup attempt; a subsequent retry confirms remaining resources.

Initial disposable cluster creation uses `initdb --no-sync` to avoid waiting for
its initial disk flush. PostgreSQL server `fsync` remains enabled for test
workloads. Recreate these clusters after an operating-system crash; see the
[PostgreSQL initialization contract](https://www.postgresql.org/docs/16/app-initdb.html).

For static convenience, use `Tt.CreateLeaseAsync<TContext>()` and await
`Tt.DisposeAsync()` after all leases finish. Static owners are separate per
context type. Configure before the first static create; configuration changes
require final disposal first. A subsequent create constructs a valid new owner.
Independent factories have separate configuration and shutdown.

Hard capacity bounds and verified restoration are implemented. Recovery and
drain verification is recorded in the [tracker](docs/issues/README.md).
DI ownership remains under
[TT-007](docs/issues/open/api/TT-007-use-the-configured-owner-in-di-and-static-apis.md).

## Prepared fixture baselines

The baseline overload prepares schema and seed data once before returning a
factory. Each lease receives a validated copy. Ordinary returns now restore
cached seed rows and sequence state in place; schema changes, foreign-key cycles
and oversized seed caches use the sealed template. Baselines and row restoration
have passed independent completion review under TT-003 and TT-004.

```csharp
var baseline = new TtBaselineDefinition<MyDbContext>(
    "users-v1", TtSchemaInitialization.EnsureCreated,
    async (context, cancellation) =>
    {
        context.Users.Add(new User { Email = "seed@example.com" });
        await context.SaveChangesAsync(cancellation);
    });
await using var fixture = await Tt.CreateFactoryAsync(baseline, options => options.PoolSize = 1);
await using var lease = await fixture.CreateLeaseAsync();
var seededUsers = await lease.Context.Users.ToListAsync();
```

Choose `Migrations` to run EF migrations or `Caller` to initialize schema in the
callback. This explicit mode takes precedence over legacy `AutoMigrate`.
`ConfigureContext` belongs to the baseline definition and runs once. Contexts
must retain their fixture's source and frozen model, including in
`OnConfiguring`. Supplied runtime models must have table/column mappings matching
the design-time schema model for `EnsureCreated` or migrations.

`PoolSize` is the fixed workload database cap. Preparing, leased, resetting,
quarantined and retiring slots retain capacity until cleanup is confirmed.
Prepared fixtures use one host; ordinary fixtures use one container per slot.
At capacity, `CreateLeaseAsync(cancellationToken: token)` waits for a verified
Ready slot. Supply a deadline with `CancellationTokenSource(TimeSpan)`; there
is no implicit acquisition timeout and cancellation does not create overflow.
`fixture.PoolState` reports reservations, queued acquisitions, waiting resets
and executing work. `fixture.ResourceBudget` reports the captured ceilings.

`PreparationConcurrency` and `ResetConcurrency` independently limit executing
work (both default to one). `MaxConnectionsPerDatabase` limits each owned
workload source (default four); prepared fixtures also share
`MaxAdministratorConnections` for private control and verification connections
(default two). The managed connection ceiling is slots times per-database
connections plus that administrator allowance. Baseline initialization drains
before workload warming. Prepared fixtures own two fixed infrastructure
databases in addition to their workload slots. PostgreSQL built-in databases,
background processes and independently created test-client sources are outside
these counts. `MaxContainers` defaults to the ordinary slot cap or one prepared
host; a ceiling too small for the configured fixed capacity is rejected.

`DatabaseName` is a generated-name prefix, limited to 21 UTF-8 bytes;
`Username` may occupy at most 63 UTF-8 bytes. Options are copied before use.
Automatic allocation and custom connection-string templates are rejected by
the prepared path. Ordinary startup allocation respects `MaxContainers`.
Dynamic scaling is rejected until the bounded adaptive controller is available.

Failed reset or validation quarantines the slot. Confirmed deletion starts a
replacement even without queued demand; schema, seed, sequences and identity
must verify before it becomes Ready. Recovery permits at most three preparation
attempts with 100 ms spacing. Unconfirmed deletion keeps the reservation and
permits at most three deletion attempts; cleanup retries cannot restart the
preparation budget. Initial startup preparation fails once and drains rather
than starting recovery. Terminal exhaustion settles waiters with a lifecycle
diagnostic; retained resources require explicit cleanup. No retry creates an
overflow reservation.

Supported baselines contain permanent ordinary tables, namespaces, indexes,
constraints, their catalog comments and sequences. Views, routines, custom types/extensions, user
triggers/rules, RLS/policies, partition/inheritance structures, foreign/unlogged
relations, collations, extended statistics, text-search objects, operators,
conversions and casts involving user types are rejected. Custom database
grants/settings/comments, `IS_TEMPLATE` and role defaults are also rejected. All
permanent sequences are checked, including unused and standalone sequences,
their ownership dependencies, `last_value` and `is_called`. PostgreSQL binary
row hashes validate seed contents on the owning host/version. No seed payloads
are retained in diagnostics. Row reset caches at most 16 MiB of raw binary seed
streams per fixture, including COPY framing; larger seeds use recreation.
Generated columns are recomputed, and supported non-public schemas participate
in both cleanup and validation. Reset drains only that slot's cached sessions,
restores child/parent ordering and checks the complete baseline before reuse.

When a supplied logger enables Debug, event `400` (`BaselineReset`) reports
`ResetPermitWaitMilliseconds`, `ResetExecutionMilliseconds`,
`ValidationMilliseconds`, `Strategy` and `Succeeded`, with an opaque owner ID.
Execution includes restoration and validation; permit wait is separate.
Preparation and reset use their independent configured concurrency limits.
Diagnostic emission is outside measured work.

If startup or drain cleanup fails, `TtLifecycleException.Cleanup` retains an opaque owner
and resource-ID snapshot. Await `error.Cleanup.RetryCleanupAsync()` to retry;
concurrent retries share an attempt. `Tt.DisposeAsync()` also retries registered
failed owners. Resources remain accounted until confirmed deletion, and a
closed owner cannot publish another lease.

## Requirements

- .NET 10.0 or later
- Docker Desktop or Docker Engine
- Entity Framework Core 10.0+

## License

MIT

## Contributing

Issues and contributions are welcome.

The current implementation backlog and investigation evidence are in the
[repository ticket tracker](docs/issues/README.md).

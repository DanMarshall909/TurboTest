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
Factory startup observes cancellation at its startup checkpoints and retains
cleanup ownership during an in-flight startup operation.

Initial disposable cluster creation uses `initdb --no-sync` to avoid waiting for
its initial disk flush. PostgreSQL server `fsync` remains enabled for test
workloads. Recreate these clusters after an operating-system crash; see the
[PostgreSQL initialization contract](https://www.postgresql.org/docs/16/app-initdb.html).

For static convenience, use `Tt.CreateLeaseAsync<TContext>()` and await
`Tt.DisposeAsync()` after all leases finish. Static owners are separate per
context type. Configure before the first static create; configuration changes
require final disposal first. A subsequent create constructs a valid new owner.
Independent factories have separate configuration and shutdown.

Hard capacity bounds, row restoration and recovery policy remain work in the
[tracker](docs/issues/README.md). DI ownership remains under
[TT-007](docs/issues/open/api/TT-007-use-the-configured-owner-in-di-and-static-apis.md).

## Prepared fixture baselines

The baseline overload prepares schema and seed data once before returning a
factory. Each lease receives a validated copy; return currently recreates that
copy from the sealed template. This path is under TT-003 verification and has
not yet passed independent completion review.

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

Prepared fixtures use one host and `PoolSize` initial prepared slots.
`DatabaseName` is a generated-name prefix, limited to 21 UTF-8 bytes;
`Username` may occupy at most 63 UTF-8 bytes. Options are copied before use.
Automatic allocation, dynamic scaling and custom connection-string templates
are currently rejected by this path. `PoolSize` is not yet a hard capacity cap.

Supported baselines contain permanent ordinary tables, namespaces, indexes,
constraints, their catalog comments and sequences. Views, routines, custom types/extensions, user
triggers/rules, RLS/policies, partition/inheritance structures, foreign/unlogged
relations, collations, extended statistics and text-search objects are rejected.
Custom database grants/settings and role defaults are also rejected. All
permanent sequences are checked, including unused and standalone sequences,
their ownership dependencies, `last_value` and `is_called`. PostgreSQL binary
row hashes validate seed contents on the owning host/version. No seed payloads
are retained in diagnostics.

If startup cleanup fails, `TtLifecycleException.Cleanup` retains an opaque owner
and resource-ID snapshot. Await `error.Cleanup.RetryCleanupAsync()` to retry;
concurrent retries share an attempt. `Tt.DisposeAsync()` also retries registered
failed startups. Resources remain accounted until confirmed deletion, and a
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

using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedRetentionTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Expired_parked_database_retires_at_monotonic_boundary_before_renewal_can_use_its_capacity()
    {
        var clock = new ManualPoolClock();
        var lifetime = TimeSpan.FromSeconds(10);
        var dropping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drops = 0;
        await using var fixture = await Tt.CreatePreparedFactoryAsync(
            new TtOptions { PoolSize = 1, IdleDatabaseLifetime = lifetime },
            new TtBaselineDefinition<TestDbContext>("idle-expiry-v1", TtSchemaInitialization.EnsureCreated,
                async (context, token) =>
                {
                    context.Users.Add(new User { Email = "expired-slot@example.test" });
                    await context.SaveChangesAsync(token);
                }), default, dropDatabase: async (connection, name) =>
                {
                    if (Interlocked.Increment(ref drops) == 1)
                    {
                        dropping.TrySetResult();
                        await allowDrop.Task;
                    }
                    await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(name)} WITH (FORCE)", connection);
                    await command.ExecuteNonQueryAsync();
                }, timeProvider: clock);
        var original = await fixture.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(original.Context);
        await original.DisposeAsync();
        clock.Advance(lifetime - TimeSpan.FromTicks(1));
        await fixture.MaintainIdleAsync();
        Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.Parked, drops));
        clock.JumpWallClock(TimeSpan.FromDays(100));
        await fixture.MaintainIdleAsync();
        Assert.Equal(0, drops);
        clock.Advance(TimeSpan.FromTicks(1));
        var maintenance = fixture.MaintainIdleAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<TtLease<TestDbContext>>? waiting = null;
        try
        {
            Assert.False(maintenance.IsCompleted);
            await dropping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.Retiring, fixture.PoolState.Parked));
            waiting = fixture.CreateLeaseAsync(cancellationToken: cancel.Token);
            Assert.False(waiting.IsCompleted);
            Assert.Equal(0, fixture.PoolState.Preparing);
            allowDrop.TrySetResult();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(15));
            await using var renewed = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
            var fresh = await PooledResetTests.ReadIdentityAsync(renewed.Context);
            Assert.NotEqual(identity.Oid, fresh.Oid);
            Assert.NotEqual(identity.Database, fresh.Database);
            Assert.Equal("expired-slot@example.test", (await renewed.Context.Users.SingleAsync()).Email);
            Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.Leased, fixture.PoolState.Retiring));
        }
        finally
        {
            cancel.Cancel();
            allowDrop.TrySetResult();
            await Record.ExceptionAsync(() => maintenance);
            if (waiting is not null) await Record.ExceptionAsync(async () => { await using var lease = await waiting; });
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Retained_limit_preserves_the_ready_floor_and_renewed_demand_waits_for_real_database_deletion()
    {
        var dropping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowDrop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var drops = 0;
        string? target = null;
        var options = new TtOptions { PoolSize = 2, WarmFloor = 1, MaxParkedDatabases = 0 };
        await using var fixture = await Tt.CreatePreparedFactoryAsync(options,
            new TtBaselineDefinition<TestDbContext>("retained-limit-v1", TtSchemaInitialization.EnsureCreated,
                async (context, token) =>
                {
                    context.Users.Add(new User { Email = "retained-limit@example.test" });
                    await context.SaveChangesAsync(token);
                }), default, dropDatabase: async (connection, name) =>
                {
                    if (Interlocked.Increment(ref drops) == 1)
                    {
                        target = name;
                        dropping.TrySetResult();
                        await allowDrop.Task;
                    }
                    await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(name)} WITH (FORCE)", connection);
                    await command.ExecuteNonQueryAsync();
                });
        var first = await fixture.CreateLeaseAsync();
        var second = await fixture.CreateLeaseAsync();
        var original = new[] { await PooledResetTests.ReadIdentityAsync(first.Context), await PooledResetTests.ReadIdentityAsync(second.Context) };
        await first.DisposeAsync();
        await second.DisposeAsync();
        // Caller mutation cannot change the captured maintenance policy.
        options.MaxParkedDatabases = 2;
        options.WarmFloor = 2;
        var maintenance = fixture.MaintainIdleAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        TtLease<TestDbContext>? retained = null;
        Task<TtLease<TestDbContext>>? waiting = null;
        try
        {
            await dropping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(maintenance.IsCompleted);
            Assert.Equal((2, 1, 0, 1), (fixture.PoolState.Reserved, fixture.PoolState.Ready, fixture.PoolState.Parked, fixture.PoolState.Retiring));
            retained = await fixture.CreateLeaseAsync(cancellationToken: cancel.Token);
            var retainedIdentity = await PooledResetTests.ReadIdentityAsync(retained.Context);
            Assert.Contains((retainedIdentity.Database, retainedIdentity.Oid), original.Select(identity => (identity.Database, identity.Oid)));
            await retained.Context.Database.OpenConnectionAsync();
            await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = $1",
                (NpgsqlConnection)retained.Context.Database.GetDbConnection());
            query.Parameters.Add(new NpgsqlParameter { Value = target! });
            Assert.Equal(1L, await query.ExecuteScalarAsync());
            waiting = fixture.CreateLeaseAsync(cancellationToken: cancel.Token);
            Assert.False(waiting.IsCompleted);
            Assert.Equal((2, 0, 1), (fixture.PoolState.Reserved, fixture.PoolState.Preparing, fixture.PoolState.QueuedAcquisitions));
            allowDrop.TrySetResult();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(15));
            await using var renewed = await waiting.WaitAsync(TimeSpan.FromSeconds(10));
            var renewedIdentity = await PooledResetTests.ReadIdentityAsync(renewed.Context);
            Assert.DoesNotContain(renewedIdentity.Database, original.Select(identity => identity.Database));
            Assert.DoesNotContain(renewedIdentity.Oid, original.Select(identity => identity.Oid));
            Assert.Equal(0L, await query.ExecuteScalarAsync());
            Assert.Equal("retained-limit@example.test", (await renewed.Context.Users.SingleAsync()).Email);
            Assert.Equal((2, 2, 0), (fixture.PoolState.Reserved, fixture.PoolState.Leased, fixture.PoolState.Parked));
        }
        finally
        {
            cancel.Cancel();
            allowDrop.TrySetResult();
            await Record.ExceptionAsync(() => maintenance);
            if (waiting is not null) await Record.ExceptionAsync(async () => { await using var lease = await waiting; });
            if (retained is not null) await retained.DisposeAsync();
        }
    }
}

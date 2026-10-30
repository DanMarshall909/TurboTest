using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class PreparedAdaptiveTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Growth_canceled_while_waiting_for_an_administrator_is_cooperative_shutdown()
    {
        var resetting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pool = new TtPreparedPool<TestDbContext>(new TtOptions
        {
            PoolSize = 3, StartupWarmDatabases = 2, EnableDynamicScaling = true, MaxAdministratorConnections = 1,
            AdaptivePool = new() { MinimumWarmDatabases = 2, GrowthStabilization = TimeSpan.FromMilliseconds(20),
                CheckInterval = TimeSpan.FromMilliseconds(10) }
        }, new TtBaselineDefinition<TestDbContext>("adaptive-admin-cancel-v1", TtSchemaInitialization.EnsureCreated),
            afterRowCommit: async _ => { resetting.TrySetResult(); await release.Task; });
        await pool.InitializeAsync(default);
        var first = await pool.GetDatabaseAsync();
        _ = await pool.GetDatabaseAsync(); // The direct pool owns this bare reservation during drain.
        var returning = first.DisposeAsync().AsTask();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waiting = pool.GetDatabaseAsync(deadline.Token);
        try
        {
            await resetting.Task.WaitAsync(deadline.Token);
            while (pool.Snapshot.Preparing != 1 || pool.Snapshot.WaitingAdministratorConnections != 1)
                await Task.Delay(10, deadline.Token);
            var drain = pool.DisposeAsync().AsTask();
            Assert.False(drain.IsCompleted);
            release.TrySetResult();
            await returning.WaitAsync(deadline.Token);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            await drain.WaitAsync(deadline.Token);
            Assert.Equal(0, pool.Snapshot.Reserved);
            Assert.Empty(pool.OwnedResourceIds);
        }
        finally
        {
            release.TrySetResult();
            deadline.Cancel();
            await Record.ExceptionAsync(() => waiting);
            await Record.ExceptionAsync(() => returning);
            await Record.ExceptionAsync(() => pool.DisposeAsync().AsTask());
            await pool.RetryCleanupAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Shutdown_preserves_a_late_automatic_retirement_failure_after_slots_and_host_are_deleted()
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions
        {
            PoolSize = 2, StartupWarmDatabases = 2, EnableDynamicScaling = true,
            MaxParkedDatabases = 0, DrainTimeout = TimeSpan.FromMilliseconds(100),
            AdaptivePool = new() { ShrinkStabilization = TimeSpan.FromMilliseconds(50), CheckInterval = TimeSpan.FromMilliseconds(10) }
        }, new TtBaselineDefinition<TestDbContext>("adaptive-retire-v1", TtSchemaInitialization.EnsureCreated), default,
            dropDatabase: async (connection, name) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt > 3) throw new InvalidOperationException("Deletion exceeded two owned databases plus one failed attempt.");
                if (attempt == 1) { deleting.TrySetResult(); await release.Task; }
                var target = attempt == 1 ? connection.Database : name;
                await using var command = new NpgsqlCommand(
                    $"DROP DATABASE IF EXISTS {new NpgsqlCommandBuilder().QuoteIdentifier(target)} WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            });
        TtCleanup? cleanup = null;
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var timeout = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.DisposeAsync().AsTask());
            Assert.Equal("DrainTimeout", timeout.Stage);
            cleanup = Assert.IsType<TtCleanup>(timeout.Cleanup);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            Assert.Single(cleanup.OwnedResourceIds);
            release.TrySetResult();
            var late = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Contains("55006", late.Message);
            Assert.DoesNotContain("DROP DATABASE", late.ToString());
            Assert.Null(late.InnerException);
            Assert.Equal((0, 3), (fixture.PoolState.Reserved, attempts));
            Assert.Empty(fixture.OwnedResourceIds);
            await cleanup.RetryCleanupAsync();
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Equal(3, attempts);
        }
        finally
        {
            release.TrySetResult();
            var failure = await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Shutdown_joins_admitted_growth_and_retains_a_late_material_failure_before_final_cleanup(bool materialFailure)
    {
        var verifying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;
        var verifications = 0;
        var hostDeletions = 0;
        var options = new TtOptions
        {
            PoolSize = 2, EnableDynamicScaling = true, DrainTimeout = TimeSpan.FromMilliseconds(100),
            AdaptivePool = new() { GrowthStabilization = TimeSpan.FromMilliseconds(20), CheckInterval = TimeSpan.FromMilliseconds(10) }
        };
        var fixture = await Tt.CreatePreparedFactoryAsync(options,
            new TtBaselineDefinition<TestDbContext>("adaptive-drain-v1", TtSchemaInitialization.EnsureCreated), default,
            beforeVerification: async _ =>
            {
                var attempt = Interlocked.Increment(ref verifications);
                if (attempt == 1) return;
                if (attempt > 2) throw new InvalidOperationException("Shutdown admitted a new preparation.");
                verifying.TrySetResult();
                await release.Task;
                finished = true;
                if (materialFailure) throw new IOException("private-late-growth-detail");
            }, deleteHost: async container =>
            {
                try
                {
                    Assert.True(!verifying.Task.IsCompleted || finished);
                    Assert.Equal(1, Interlocked.Increment(ref hostDeletions));
                }
                finally { await container.DisposeAsync(); }
            });
        using var waitingCancellation = new CancellationTokenSource();
        TtLease<TestDbContext>? lease = null;
        Task<TtLease<TestDbContext>>? waiting = null;
        TtCleanup? cleanup = null;
        try
        {
            lease = await fixture.CreateLeaseAsync();
            waiting = fixture.CreateLeaseAsync(cancellationToken: waitingCancellation.Token);
            await verifying.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var host = Assert.Single(fixture.OwnedResourceIds);
            var timeout = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.DisposeAsync().AsTask());
            Assert.Equal("DrainTimeout", timeout.Stage);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            cleanup = Assert.IsType<TtCleanup>(timeout.Cleanup);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal(host, Assert.Single(cleanup.OwnedResourceIds));
            Assert.Equal(0, hostDeletions);
            release.TrySetResult();
            if (materialFailure)
            {
                var late = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.Contains("IOException", late.Message);
                Assert.DoesNotContain("private-late-growth-detail", late.ToString());
                Assert.Null(late.InnerException);
            }
            else await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await cleanup.RetryCleanupAsync();
            Assert.Equal((0, 2, 1), (fixture.PoolState.Reserved, verifications, hostDeletions));
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            await FinishGrowthFixtureAsync(fixture, lease, waiting, waitingCancellation, release, cleanup);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_failed_growth_observation_still_settles_queued_demand_and_deletes_its_host()
    {
        var fixture = await Tt.CreateFactoryAsync(
            new TtBaselineDefinition<TestDbContext>("adaptive-no-growth-cleanup-v1", TtSchemaInitialization.EnsureCreated),
            options =>
            {
                options.PoolSize = 2;
                options.EnableDynamicScaling = true;
                options.AdaptivePool = new() { GrowthStabilization = TimeSpan.FromDays(1), CheckInterval = TimeSpan.FromMilliseconds(10) };
            });
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TtLease<TestDbContext>? lease = null;
        Task<TtLease<TestDbContext>>? waiting = null;
        Task? cleanup = null;
        try
        {
            lease = await fixture.CreateLeaseAsync();
            waiting = fixture.CreateLeaseAsync(cancellationToken: cancellation.Token);
            await Assert.ThrowsAsync<TimeoutException>(() => waiting.WaitAsync(TimeSpan.FromMilliseconds(100)));
            Assert.False(waiting.IsCompleted);
            Assert.Single(fixture.OwnedResourceIds);
            cleanup = FinishGrowthFixtureAsync(fixture, lease, waiting, cancellation, release, null);
            await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(waiting.IsCanceled);
            Assert.Empty(fixture.OwnedResourceIds);
            Assert.Equal(0, fixture.PoolState.Reserved);
        }
        finally
        {
            // The regression's own rescue runs even if the shared cleanup hangs.
            cancellation.Cancel();
            release.TrySetResult();
            if (cleanup is not null)
                await Record.ExceptionAsync(() => cleanup.WaitAsync(TimeSpan.FromSeconds(15)));
            await FinishGrowthFixtureAsync(fixture, lease, waiting, cancellation, release, null)
                .WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task FinishGrowthFixtureAsync(TtFactory<TestDbContext> fixture,
        TtLease<TestDbContext>? lease, Task<TtLease<TestDbContext>>? waiting,
        CancellationTokenSource cancellation, TaskCompletionSource release, TtCleanup? cleanup)
    {
        release.TrySetResult();
        cancellation.Cancel();
        try
        {
            if (waiting is not null)
            {
                var failure = await Record.ExceptionAsync(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.IsNotType<TimeoutException>(failure);
            }
        }
        finally
        {
            try
            {
                if (lease is not null) await lease.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                var failure = await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.IsNotType<TimeoutException>(failure);
                cleanup ??= (failure as TtLifecycleException)?.Cleanup;
                if (cleanup is not null)
                {
                    await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                    await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_adaptive_fixture_grows_one_to_eight_parks_to_one_and_reuses_all_eight_after_idle()
    {
        await using var fixture = await Tt.CreateFactoryAsync(
            new TtBaselineDefinition<TestDbContext>("adaptive-public-v1", TtSchemaInitialization.EnsureCreated,
                async (context, token) =>
                {
                    context.Users.Add(new User { Email = "adaptive-seed@example.test" });
                    await context.SaveChangesAsync(token);
                }), options =>
            {
                options.PoolSize = 8;
                options.StartupWarmDatabases = 1;
                options.MemoryLimitMb = 512;
                options.EnableDynamicScaling = true;
                options.AdaptivePool = new()
                {
                    MinimumWarmDatabases = 1, MaximumWarmDatabases = 8,
                    GrowthStabilization = TimeSpan.FromMilliseconds(100),
                    ShrinkStabilization = TimeSpan.FromMilliseconds(300),
                    Cooldown = TimeSpan.Zero, CheckInterval = TimeSpan.FromMilliseconds(20)
                };
            });
        Assert.Equal((8, 1, 1, 1), (fixture.PoolState.SlotLimit, fixture.PoolState.Reserved,
            fixture.PoolState.Ready, fixture.PoolState.WarmTarget));
        var leases = new List<TtLease<TestDbContext>>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task<TtLease<TestDbContext>>[] acquiring = [];
        try
        {
            leases.Add(await fixture.CreateLeaseAsync(cancellationToken: deadline.Token));
            acquiring = Enumerable.Range(0, 7).Select(_ => fixture.CreateLeaseAsync(cancellationToken: deadline.Token)).ToArray();
            leases.AddRange(await Task.WhenAll(acquiring));
            Assert.Equal((8, 8, 8), (fixture.PoolState.WarmTarget, fixture.PoolState.Reserved, fixture.PoolState.Leased));
            var original = new Dictionary<string, uint>();
            for (var i = 0; i < leases.Count; i++)
            {
                var identity = await PooledResetTests.ReadIdentityAsync(leases[i].Context);
                original.Add(identity.Database, identity.Oid);
                Assert.Equal("adaptive-seed@example.test", (await leases[i].Context.Users.SingleAsync()).Email);
                leases[i].Context.Users.Add(new User { Email = $"private-marker-{i}@example.test" });
                await leases[i].Context.SaveChangesAsync();
            }
            for (var i = 1; i < leases.Count; i++) await leases[i].DisposeAsync();
            await UntilAsync(() => fixture.PoolState.WarmTarget == 1 && fixture.PoolState.Parked == 7);
            Assert.Equal((1, 0, 8), (fixture.PoolState.Leased, fixture.PoolState.Ready, fixture.PoolState.Reserved));
            await leases[0].DisposeAsync();
            leases.Clear();
            await UntilAsync(() => fixture.PoolState.Ready == 1);
            acquiring = Enumerable.Range(0, 8).Select(_ => fixture.CreateLeaseAsync(cancellationToken: deadline.Token)).ToArray();
            leases.AddRange(await Task.WhenAll(acquiring));
            var renewed = new Dictionary<string, uint>();
            foreach (var lease in leases)
            {
                var identity = await PooledResetTests.ReadIdentityAsync(lease.Context);
                renewed.Add(identity.Database, identity.Oid);
                Assert.Equal("adaptive-seed@example.test", (await lease.Context.Users.SingleAsync()).Email);
                lease.Context.Users.Add(new User { Email = "renewed-marker@example.test" });
                await lease.Context.SaveChangesAsync();
                Assert.Equal(2, await lease.Context.Users.MaxAsync(user => user.Id));
            }
            Assert.Equal(original.OrderBy(pair => pair.Key), renewed.OrderBy(pair => pair.Key));
            Assert.Equal((8, 8, 0), (fixture.PoolState.WarmTarget, fixture.PoolState.Leased, fixture.PoolState.Parked));
        }
        finally
        {
            deadline.Cancel();
            foreach (var task in acquiring)
                await Record.ExceptionAsync(async () => { await using var lease = await task; });
            foreach (var lease in leases) await lease.DisposeAsync();
        }
        await fixture.DisposeAsync();
        Assert.Equal(0, fixture.PoolState.Reserved);
        Assert.Empty(fixture.OwnedResourceIds);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.CreateLeaseAsync());

        async Task UntilAsync(Func<bool> condition)
        {
            while (!condition())
            {
                Assert.InRange(fixture.PoolState.Reserved, 1, 8);
                Assert.InRange(fixture.PoolState.WarmTarget, 1, 8);
                await Task.Delay(20, deadline.Token);
            }
        }
    }
}

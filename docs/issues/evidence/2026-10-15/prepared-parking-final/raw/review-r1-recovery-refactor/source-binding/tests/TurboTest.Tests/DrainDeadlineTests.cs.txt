using Microsoft.EntityFrameworkCore;
using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class DrainDeadlineTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_timed_out_prepared_drain_retains_the_actual_reset_session_until_return_settles()
    {
        var prefix = $"tt-reset-drain-{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetSession = 0;
        var resetCalls = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("held-reset-drain-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) =>
            {
                context.Users.Add(new User { Email = "retained-reset@example.test" });
                await context.SaveChangesAsync(token);
            });
        var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions
        {
            PoolSize = 1, ContainerPrefix = prefix, Password = password,
            DrainTimeout = TimeSpan.FromMilliseconds(100)
        }, definition, default, afterSequenceRestore: async (connection, transaction) =>
        {
            if (++resetCalls > 1) throw new InvalidOperationException("Pending reset was repeated.");
            await using var query = new NpgsqlCommand("SELECT pg_backend_pid()", connection, transaction);
            resetSession = (int)(await query.ExecuteScalarAsync())!;
            entered.TrySetResult();
            await release.Task;
        });
        var lease = await fixture.CreateLeaseAsync();
        var observerOptions = new NpgsqlConnectionStringBuilder(lease.Context.Database.GetDbConnection().ConnectionString)
        {
            Password = password, Pooling = false, Enlist = false
        };
        await lease.Context.Users.ExecuteDeleteAsync();
        var returning = lease.DisposeAsync().AsTask();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var original = Assert.Single(await OwnedAsync()).ID;
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.DisposeAsync().AsTask());
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.ExecutingResets, fixture.PoolState.Ready));
            Assert.False(returning.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.CreateLeaseAsync());
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            await using (var observer = new NpgsqlConnection(observerOptions.ConnectionString))
            {
                await observer.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE pid = $1", observer);
                query.Parameters.Add(new NpgsqlParameter { Value = resetSession });
                Assert.Equal(1L, await query.ExecuteScalarAsync());
            }
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            await returning.WaitAsync(TimeSpan.FromSeconds(10));
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal((1, 0, 0), (resetCalls, fixture.PoolState.Reserved, fixture.PoolState.Ready));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => returning);
            var failure = await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
        }

        Task<IList<ContainerListResponse>> OwnedAsync() => docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_timed_out_native_drain_preserves_the_live_session_until_the_callback_finishes()
    {
        var prefix = $"tt-drain-deadline-{Guid.NewGuid():N}";
        var password = Guid.NewGuid().ToString("N");
        var fixture = await Tt.CreateFactoryAsync<BlockingContext>(options =>
        {
            options.PoolSize = 1;
            options.AutoMigrate = false;
            options.ContainerPrefix = prefix;
            options.Password = password;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        var lease = await fixture.CreateLeaseAsync();
        var context = lease.Context;
        var observerOptions = new NpgsqlConnectionStringBuilder(context.Database.GetDbConnection().ConnectionString)
        {
            Password = password, Pooling = false, Enlist = false
        };
        await context.Database.OpenConnectionAsync();
        var session = await context.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var original = Assert.Single(await OwnedAsync()).ID;
        TtCleanup? cleanup = null;
        Task? closing = null;
        try
        {
            closing = fixture.DisposeAsync().AsTask();
            await context.Closing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            await using (var observer = new NpgsqlConnection(observerOptions.ConnectionString))
            {
                await observer.OpenAsync();
                await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE pid = $1", observer);
                query.Parameters.Add(new NpgsqlParameter { Value = session });
                Assert.Equal(1L, await query.ExecuteScalarAsync());
            }
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            context.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Empty(await OwnedAsync());
            Assert.Equal((0, 1), (fixture.PoolState.Reserved, context.Attempts));
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            context.Release.TrySetResult();
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            else await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask());
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
        }

        Task<IList<ContainerListResponse>> OwnedAsync() => docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });
    }

    [Fact]
    public async Task Drain_timeout_retains_the_pending_callback_and_concurrent_cleanup_shares_that_work()
    {
        await using var pool = new OwnedPool();
        var options = new TtOptions { PoolSize = 1, AutoMigrate = false, DrainTimeout = TimeSpan.FromMilliseconds(100) };
        var fixture = new TtFactory<BlockingContext>(pool, options, ownsPool: true);
        var lease = await fixture.CreateLeaseAsync();
        var context = lease.Context;
        var closing = fixture.DisposeAsync().AsTask();
        TtCleanup? cleanup = null;
        try
        {
            await context.Closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", error.Stage);
            cleanup = Assert.IsType<TtCleanup>(error.Cleanup);
            Assert.NotEmpty(cleanup.OwnedResourceIds);
            Assert.Equal((1, 0, 1), (pool.Snapshot.Reserved, pool.Deleted, context.Attempts));
            Assert.Same(error, await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.CreateLeaseAsync());
            var firstRetry = cleanup.RetryCleanupAsync();
            var concurrentRetry = cleanup.RetryCleanupAsync();
            Assert.Same(firstRetry, concurrentRetry);
            Assert.False(firstRetry.IsCompleted);
            Assert.Equal((0, 1), (pool.Deleted, context.Attempts));
            context.Release.TrySetResult();
            await firstRetry.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((0, 1, 1), (pool.Snapshot.Reserved, pool.Deleted, context.Attempts));
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            context.Release.TrySetResult();
            await Record.ExceptionAsync(() => closing);
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
        }
    }

    public sealed class BlockingContext(DbContextOptions<BlockingContext> options) : DbContext(options)
    {
        internal TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            Attempts++;
            Closing.TrySetResult();
            await Release.Task;
            await base.DisposeAsync();
        }
    }

    private sealed class OwnedPool : ITtDatabasePool
    {
        private readonly TtSlotPool<NpgsqlDataSource> _slots;
        internal int Deleted { get; private set; }
        public TtPoolSnapshot Snapshot => _slots.Snapshot;
        public TtResourceBudget Budget => TtResourceBudget.Create(new TtOptions { PoolSize = 1 }, false);
        public IReadOnlyList<string> OwnedResourceIds => Snapshot.Reserved == 0 ? Array.Empty<string>() : [Snapshot.OwnerId.ToString("N")];

        internal OwnedPool() => _slots = new TtSlotPool<NpgsqlDataSource>(1, 1,
            _ => NpgsqlDataSource.Create("Host=unused;Database=unused;Username=unused"),
            (_, _) => Task.CompletedTask,
            async source => { await source.DisposeAsync(); Deleted++; });

        public async Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
        {
            var reservation = await _slots.AcquireAsync(cancellationToken);
            return new TtDatabase(reservation.Value,
                () => _slots.ReturnAsync(reservation, (_, _) => Task.CompletedTask),
                () => _slots.DiscardAsync(reservation));
        }

        public ValueTask DisposeAsync() => _slots.DisposeAsync();
    }
}

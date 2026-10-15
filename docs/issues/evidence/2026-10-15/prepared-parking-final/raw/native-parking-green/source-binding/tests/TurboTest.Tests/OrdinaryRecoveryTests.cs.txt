using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class OrdinaryRecoveryTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Transient_cleanup_failure_retains_the_container_until_delete_then_restores_verified_capacity()
    {
        var prefix = $"tt-recover-drop-{Guid.NewGuid():N}";
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? originalId = null;
        var attempts = 0;
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix }, async container =>
        {
            if (container.Id == originalId)
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt > 2) throw new InvalidOperationException("Confirmed cleanup was repeated.");
                if (attempt == 1) throw new IOException("Injected unconfirmed deletion.");
                retryStarted.TrySetResult();
                await releaseRetry.Task;
            }
            await container.DisposeAsync();
        });
        await pool.InitializeAsync();
        originalId = Assert.Single(pool.OwnedResourceIds);
        var dirty = await pool.GetDatabaseAsync();
        string originalDatabase;
        await using (var connection = await dirty.DataSource.OpenConnectionAsync())
        {
            originalDatabase = connection.Database;
            await using var command = new NpgsqlCommand("""
                CREATE TABLE broken_reset(id integer);
                CREATE FUNCTION reject_reset() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'injected reset rejection'; END $$;
                CREATE TRIGGER reject_reset BEFORE TRUNCATE ON broken_reset
                FOR EACH STATEMENT EXECUTE FUNCTION reject_reset();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var waiting = pool.GetDatabaseAsync(deadline.Token);
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => dirty.DisposeAsync().AsTask());
            Assert.Equal(("ResetRows", "P0001"), (failure.Stage, failure.SqlState));
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1), (pool.Snapshot.Reserved, pool.Snapshot.Retiring));
            Assert.Equal(originalId, Assert.Single(await OwnedAsync()).ID);
            releaseRetry.TrySetResult();
            await using var clean = await waiting;
            await using var connection = await clean.DataSource.OpenConnectionAsync();
            Assert.NotEqual(originalDatabase, connection.Database);
            await using var query = new NpgsqlCommand("SELECT to_regclass('broken_reset') IS NULL", connection);
            Assert.Equal(true, await query.ExecuteScalarAsync());
            Assert.NotEqual(originalId, Assert.Single(await OwnedAsync()).ID);
            Assert.Equal((1, 1, 2), (pool.Snapshot.Reserved, pool.Snapshot.Leased, attempts));
        }
        finally
        {
            deadline.Cancel();
            releaseRetry.TrySetResult();
            await Record.ExceptionAsync(async () => { await using var lease = await waiting; });
            await Record.ExceptionAsync(() => dirty.DisposeAsync().AsTask());
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
}

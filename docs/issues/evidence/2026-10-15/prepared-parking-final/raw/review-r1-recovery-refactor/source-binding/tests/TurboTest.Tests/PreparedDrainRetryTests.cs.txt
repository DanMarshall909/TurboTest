using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class PreparedDrainRetryTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_workload_deletion_retains_the_host_and_explicit_retry_confirms_complete_drain()
    {
        var prefix = $"tt-prepared-drain-{Guid.NewGuid():N}";
        var failDrop = true;
        var attempts = 0;
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 1, ContainerPrefix = prefix },
            new TtBaselineDefinition<TestDbContext>("drain-retry-v1", TtSchemaInitialization.EnsureCreated), default,
            dropDatabase: async (connection, name) =>
            {
                Interlocked.Increment(ref attempts);
                // PostgreSQL rejects DROP of the connected database (55006).
                // The intended workload remains unchanged until the permitted retry.
                var target = failDrop ? connection.Database : name;
                var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(target);
                await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {quoted} WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            });
        TtCleanup? cleanup = null;
        try
        {
            var original = Assert.Single(await OwnedAsync()).ID;
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.DisposeAsync().AsTask());
            Assert.Equal(("PreparedPoolDispose", "55006"), (failure.Stage, failure.SqlState));
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal((1, 1, 1), (attempts, fixture.PoolState.Reserved, fixture.PoolState.Quarantined));
            failDrop = false;
            await cleanup.RetryCleanupAsync();
            Assert.Equal((2, 0), (attempts, fixture.PoolState.Reserved));
            Assert.Empty(await OwnedAsync());
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Same(failure, await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask()));
        }
        finally
        {
            failDrop = false;
            if (cleanup is not null) await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
            foreach (var container in await OwnedAsync())
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
            if (cleanup is not null) TtCleanupRegistry.Release(cleanup);
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

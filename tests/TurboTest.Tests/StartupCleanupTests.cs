using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class StartupCleanupTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_failed_query_probe_closes_its_connection_before_retry()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var database = await pool.GetDatabaseAsync();
        var attempts = 0;
        await TtPool.WaitForDatabaseReadyAsync(database.DataSource, async connection =>
        {
            attempts++;
            await using var query = new NpgsqlCommand(attempts == 1 ? "SELECT 1 / 0" : "SELECT 1", connection);
            await query.ExecuteScalarAsync();
        }, maxAttempts: 2);
        Assert.Equal(2, attempts);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Disposable_cluster_skips_initial_sync_and_retains_server_fsync()
    {
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var database = await pool.GetDatabaseAsync();
        await using var connection = await database.DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SHOW fsync", connection);
        Assert.Equal("on", await command.ExecuteScalarAsync());
        // Inspect only the initialization directive; credentials stay out of assertions/output.
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true });
        var databaseName = new NpgsqlConnectionStringBuilder(database.DataSource.ConnectionString).Database;
        var matches = new List<string>();
        foreach (var container in containers.Where(c => c.Names.Any(n => n.StartsWith("/tt_"))))
        {
            var details = await docker.Containers.InspectContainerAsync(container.ID);
            if (details.Config.Env.Contains($"POSTGRES_DB={databaseName}"))
                matches.AddRange(details.Config.Env.Where(e => e.StartsWith("POSTGRES_INITDB_ARGS=")));
        }
        Assert.Equal(["POSTGRES_INITDB_ARGS=--no-sync"], matches);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_preparation_removes_every_container_it_created()
    {
        var prefix = $"tt-startup-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        await using var pool = new TtPool(new TtOptions
        {
            PoolSize = 1,
            ContainerPrefix = prefix,
            ConnectionStringTemplate = "Host=localhost;Port={1};Database=missing_database;Username={3};Password={4}"
        });
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => pool.InitializeAsync());
            await pool.DisposeAsync();
            Assert.Empty(await OwnedContainersAsync());
        }
        finally
        {
            // Also clean up a deliberately failing RED run; only this test's unique prefix is eligible.
            foreach (var container in await OwnedContainersAsync())
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }

        async Task<IList<ContainerListResponse>> OwnedContainersAsync() =>
            await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [prefix] = true }
                }
            });
    }
}

using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CompensatingCleanupTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Reset_then_failed_delete_preserves_diagnostic_and_retains_cleanup_ownership()
    {
        var prefix = $"tt-delete-{Guid.NewGuid():N}";
        var failDeletion = false;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix }, async container =>
        {
            await container.DataSource.DisposeAsync();
            if (failDeletion) throw new IOException("delete-secret");
            await container.DisposeAsync();
        });
        await pool.InitializeAsync();
        var database = await pool.GetDatabaseAsync();
        await using (var connection = await database.DataSource.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand("""
                CREATE TABLE broken_reset (id integer);
                CREATE FUNCTION reject_reset() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'reset-secret'; END $$;
                CREATE TRIGGER reject_reset BEFORE TRUNCATE ON broken_reset
                FOR EACH STATEMENT EXECUTE FUNCTION reject_reset();
                """, connection);
            await command.ExecuteNonQueryAsync();
        }
        failDeletion = true;
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => database.DisposeAsync().AsTask());
            Assert.Equal("ResetRows", error.Stage);
            Assert.Equal("P0001", error.SqlState);
            Assert.Contains("Discard", error.Message);
            Assert.Contains(nameof(IOException), error.Message);
            Assert.DoesNotContain("secret", error.ToString());
            Assert.Single(await OwnedAsync(prefix));
            var unavailable = await Assert.ThrowsAsync<TtLifecycleException>(() => pool.GetDatabaseAsync());
            Assert.Equal("ResetRows", unavailable.Stage);
            Assert.Equal("P0001", unavailable.SqlState);
            Assert.Single(await OwnedAsync(prefix));
            failDeletion = false;
            await pool.DisposeAsync();
            Assert.Empty(await OwnedAsync(prefix));
        }
        finally { failDeletion = false; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Initialization_then_failed_discard_preserves_diagnostic_and_retains_cleanup_ownership(bool cancel)
    {
        var prefix = $"tt-init-discard-{Guid.NewGuid():N}";
        var failDeletion = false;
        var options = new TtOptions { PoolSize = 1, ContainerPrefix = prefix };
        await using var pool = new TtPool(options, async container =>
        {
            await container.DataSource.DisposeAsync();
            if (failDeletion) throw new IOException("discard-secret");
            await container.DisposeAsync();
        });
        await pool.InitializeAsync();
        await using var factory = new TtFactory<PublicLeaseTests.InvalidModelContext>(pool, options);
        using var cancellation = new CancellationTokenSource();
        failDeletion = true;
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => factory.CreateLeaseAsync(
                _ => { if (cancel) cancellation.Cancel(); }, cancellation.Token));
            Assert.Equal("ContextInitialize", error.Stage);
            Assert.Null(error.SqlState);
            Assert.Contains(cancel ? nameof(OperationCanceledException) : nameof(InvalidOperationException), error.Message);
            Assert.Contains("Discard", error.Message);
            Assert.Contains(nameof(IOException), error.Message);
            Assert.DoesNotContain("secret", error.ToString());
            Assert.Single(await OwnedAsync(prefix));
            failDeletion = false;
            await pool.DisposeAsync();
            Assert.Empty(await OwnedAsync(prefix));
        }
        finally { failDeletion = false; }
    }

    private static async Task<IList<ContainerListResponse>> OwnedAsync(string prefix)
    {
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        return await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });
    }
}

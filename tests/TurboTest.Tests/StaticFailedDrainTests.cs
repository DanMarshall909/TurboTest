using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StaticFailedDrainTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Independently_failed_static_owners_keep_both_diagnostics_and_both_recovery_capabilities()
    {
        await Tt.DisposeAsync();
        var prefix = $"tt-static-multiple-{Guid.NewGuid():N}";
        Tt.Configure(options => { options.PoolSize = 1; options.ContainerPrefix = prefix; options.AutoAllocateResources = false; });
        var fail = true;
        var deleted = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        async Task DeleteAsync(TtContainer container, bool first)
        {
            if (fail)
                throw new TtLifecycleException(first ? "StaticFirstDelete" : "StaticSecondDelete", first
                    ? new Npgsql.PostgresException("private-first-delete", "ERROR", "ERROR", "53100")
                    : new IOException("private-second-delete"));
            await container.DisposeAsync();
            deleted.Add(container.Id);
        }
        try
        {
            await using var first = await Tt.CreateLeaseAsync<TestDbContext>(null, default, container => DeleteAsync(container, true));
            await using var second = await Tt.CreateLeaseAsync<SecondStaticContext>(null, default, container => DeleteAsync(container, false));
            var original = (await OwnedAsync()).Select(container => container.ID).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(2, original.Length);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("StaticFirstDelete", failure.Message);
            Assert.Contains("StaticSecondDelete", failure.Message);
            Assert.Contains("53100", failure.Message);
            Assert.Contains(nameof(IOException), failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Null(failure.InnerException);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(original, cleanup.OwnedResourceIds.Order(StringComparer.Ordinal));
            Assert.Empty(deleted);
            Assert.Equal(original, (await OwnedAsync()).Select(container => container.ID).Order(StringComparer.Ordinal));
            fail = false;
            await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(original, deleted.Order(StringComparer.Ordinal));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
            await cleanup.RetryCleanupAsync();
            await Tt.DisposeAsync();
            Assert.Equal(2, deleted.Count);
        }
        finally
        {
            fail = false;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            await Tt.DisposeAsync();
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

    public sealed class SecondStaticContext(DbContextOptions<SecondStaticContext> options) : DbContext(options)
    {
        public DbSet<User> Users { get; set; }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_failed_static_drain_keeps_its_exact_owner_for_the_next_global_cleanup()
    {
        await Tt.DisposeAsync();
        var prefix = $"tt-static-drain-{Guid.NewGuid():N}";
        Tt.Configure(options => { options.PoolSize = 1; options.ContainerPrefix = prefix; options.AutoAllocateResources = false; });
        var fail = true;
        var deleted = new List<string>();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        try
        {
            await using var lease = await Tt.CreateLeaseAsync<TestDbContext>(null, default, async container =>
            {
                if (fail) throw new IOException("Deletion remains unconfirmed.");
                await container.DisposeAsync();
                deleted.Add(container.Id);
            });
            var original = Assert.Single(await OwnedAsync()).ID;
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.DisposeAsync());
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            Assert.Empty(deleted);
            fail = false;
            await Tt.DisposeAsync();
            Assert.Equal(new[] { original }, deleted);
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
            await Tt.DisposeAsync();
            Assert.Single(deleted);
        }
        finally
        {
            fail = false;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            await Tt.DisposeAsync();
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

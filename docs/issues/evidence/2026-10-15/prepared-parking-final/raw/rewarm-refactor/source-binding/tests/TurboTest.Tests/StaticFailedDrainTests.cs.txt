using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StaticFailedDrainTests
{
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

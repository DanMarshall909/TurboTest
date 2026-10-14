using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class FailedStartupCleanupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_startup_retains_exact_cleanup_ownership_and_supports_explicit_or_global_retry(bool clone, bool global)
    {
        var prefix = $"tt-retained-startup-{Guid.NewGuid():N}";
        var failDelete = true;
        var definition = new TtBaselineDefinition<SeededBaselineTests.SeedContext>("retained-v1",
            TtSchemaInitialization.EnsureCreated, clone ? null : (_, _) => throw new IOException("initializer-secret"));
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.CreatePreparedFactoryAsync(
                new TtOptions { PoolSize = 1, ContainerPrefix = prefix }, definition, default,
                async container =>
                {
                    if (failDelete) throw new IOException("delete-secret");
                    await container.DisposeAsync();
                }, clone ? _ => throw new IOException("clone-secret") : null));
            Assert.Equal(clone ? "BaselineClone" : "BaselineInitialize", error.Stage);
            Assert.Contains("PreparedPoolDispose", error.Message);
            Assert.DoesNotContain("secret", error.ToString());
            cleanup = Assert.IsType<TtCleanup>(error.Cleanup);
            Assert.NotEqual(Guid.Empty, cleanup.OwnerId);
            var original = Assert.Single(await OwnedAsync());
            Assert.Equal([original.ID], cleanup.OwnedResourceIds);
            var repeated = await Assert.ThrowsAsync<TtLifecycleException>(() => cleanup.RetryCleanupAsync());
            Assert.Equal("PreparedPoolDispose", repeated.Stage);
            Assert.Equal([original.ID], cleanup.OwnedResourceIds);
            failDelete = false;
            if (global) await Tt.DisposeAsync();
            else await cleanup.RetryCleanupAsync();
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
            await cleanup.RetryCleanupAsync();
        }
        finally
        {
            failDelete = false;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            // A deliberate RED must also remove only its own exact resources.
            foreach (var remaining in await OwnedAsync())
                await docker.Containers.RemoveContainerAsync(remaining.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
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

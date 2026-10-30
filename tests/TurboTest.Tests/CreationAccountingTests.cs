using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CreationAccountingTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_deletion_retains_the_exact_owned_container_until_a_successful_retry()
    {
        var prefix = $"tt-delete-retry-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var rejectDeletion = true;
        var attempts = 0;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix },
            deleteResource: async container =>
            {
                Interlocked.Increment(ref attempts);
                if (rejectDeletion) throw new IOException("The owned resource was not deleted.");
                await container.DisposeAsync();
            });
        try
        {
            await pool.InitializeAsync();
            var original = Assert.Single(pool.OwnedResourceIds);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pool.DisposeAsync().AsTask());
            Assert.Equal("SlotDispose", failure.Stage);
            Assert.Equal([original], pool.OwnedResourceIds);
            Assert.Equal(1, pool.Snapshot.Reserved);
            Assert.Equal(original, (await docker.Containers.InspectContainerAsync(original)).ID);
            Assert.Equal(1, attempts);

            rejectDeletion = false;
            await pool.DisposeAsync();
            Assert.Empty(pool.OwnedResourceIds);
            Assert.Equal(0, pool.Snapshot.Reserved);
            Assert.Empty(await OwnedAsync(docker, prefix));
            Assert.Equal(2, attempts);
        }
        finally
        {
            rejectDeletion = false;
            await Record.ExceptionAsync(() => pool.DisposeAsync().AsTask());
            foreach (var container in await OwnedAsync(docker, prefix))
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Unconfirmed_absence_or_mismatched_ownership_keeps_capacity_until_the_owned_resource_is_discovered(bool mismatched)
    {
        var prefix = $"tt-uncertain-create-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        CreateContainerParameters? intended = null;
        IDictionary<string, string>? labels = null;
        var requests = 0;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix },
            createResource: async parameters =>
            {
                Interlocked.Increment(ref requests);
                intended = parameters;
                labels = new Dictionary<string, string>(parameters.Labels);
                if (mismatched)
                {
                    parameters.Labels = new Dictionary<string, string> { ["turbotest.reservation"] = Guid.NewGuid().ToString("N") };
                    await docker.Containers.CreateContainerAsync(parameters);
                }
                throw new IOException("The create response is unavailable.");
            });
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pool.InitializeAsync());
            Assert.Equal("ContainerPrepare", failure.Stage);
            Assert.Contains("Cleanup", failure.Message);
            Assert.Equal((1, 1, 0), (pool.Snapshot.Reserved, pool.Snapshot.Quarantined, pool.Snapshot.Ready));
            Assert.Equal(mismatched ? 1 : 0, (await OwnedAsync(docker, prefix)).Count);
            await Assert.ThrowsAsync<TtLifecycleException>(() => pool.GetDatabaseAsync());
            await Assert.ThrowsAsync<TtLifecycleException>(() => pool.DisposeAsync().AsTask());
            Assert.Equal((1, 1, 1), (pool.Snapshot.Reserved, pool.Snapshot.Quarantined, requests));
            var discovered = await OwnedAsync(docker, prefix);
            Assert.Equal(mismatched ? 1 : 0, discovered.Count);
            Assert.NotNull(intended);
            Assert.Contains(intended.Name, pool.OwnedResourceIds);

            // The test owns the conflicting resource. Remove it explicitly;
            // then materialize the delayed intended resource with its original label.
            foreach (var container in discovered)
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
            intended.Labels = labels;
            await docker.Containers.CreateContainerAsync(intended);
            Assert.Single(await OwnedAsync(docker, prefix));
            Assert.Equal(1, pool.Snapshot.Reserved);
            await pool.DisposeAsync();
            Assert.Empty(await OwnedAsync(docker, prefix));
            Assert.Empty(pool.OwnedResourceIds);
            Assert.Equal(0, pool.Snapshot.Reserved);
        }
        finally
        {
            await Record.ExceptionAsync(() => pool.DisposeAsync().AsTask());
            foreach (var container in await OwnedAsync(docker, prefix))
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }

    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Acknowledged_create_rejection_and_confirmed_absence_release_the_reservation()
    {
        var prefix = $"tt-rejected-create-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var requests = 0;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix },
            createResource: parameters =>
            {
                Interlocked.Increment(ref requests);
                parameters.Image = $"tt-missing-image-{Guid.NewGuid():N}";
                return docker.Containers.CreateContainerAsync(parameters);
            });
        var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pool.InitializeAsync());
        Assert.Equal("ContainerPrepare", failure.Stage);
        Assert.Equal(1, requests);
        Assert.Equal(0, pool.Snapshot.Reserved);
        Assert.Empty(pool.OwnedResourceIds);
        await pool.DisposeAsync();
        Assert.Empty(await docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        }));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_lost_create_response_does_not_release_a_container_without_confirmed_cleanup()
    {
        var prefix = $"tt-lost-create-{Guid.NewGuid():N}";
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        string? createdId = null;
        await using var pool = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = prefix },
            createResource: async parameters =>
            {
                createdId = (await docker.Containers.CreateContainerAsync(parameters)).ID;
                throw new IOException("The server created the resource but its response was lost.");
            });
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => pool.InitializeAsync());
            Assert.Equal("ContainerPrepare", error.Stage);
            Assert.NotNull(createdId);
            await pool.DisposeAsync();
            Assert.Empty(await OwnedAsync(docker, prefix));
            Assert.Equal(0, pool.Snapshot.Reserved);
        }
        finally
        {
            foreach (var container in await OwnedAsync(docker, prefix))
                await docker.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = true });
        }

    }
    private static Task<IList<ContainerListResponse>> OwnedAsync(DockerClient docker, string prefix) =>
        docker.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["name"] = new Dictionary<string, bool> { [prefix] = true }
            }
        });

}

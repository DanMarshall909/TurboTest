using TurboTest.EFCore;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StartupDeadlineTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ordinary_startup_cancellation_retains_the_created_container_until_its_pending_hook_finishes()
    {
        var prefix = $"tt-canceled-start-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? createdId = null;
        using var cancellation = new CancellationTokenSource();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var initializing = Tt.CreateOwnedFactoryAsync<TestDbContext>(
            new TtOptions { PoolSize = 1, ContainerPrefix = prefix }, cancellation.Token,
            async container => { createdId = container.Id; entered.TrySetResult(); await release.Task; });
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal("StartupCanceled", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.NotNull(createdId);
            Assert.Equal(new[] { createdId }, cleanup.OwnedResourceIds);
            Assert.False((await docker.Containers.InspectContainerAsync(createdId)).State.Running);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [prefix] = true }
                }
            }));
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(async () => { await using var fixture = await initializing; });
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Interrupted_startup_retains_the_pending_image_job_and_prevents_late_resource_creation(bool callerCancellation)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hostsDeleted = 0;
        var clones = 0;
        using var cancellation = new CancellationTokenSource();
        var initializing = Tt.CreatePreparedFactoryAsync(new TtOptions
        {
            PoolSize = 1,
            StartupTimeout = callerCancellation ? TimeSpan.FromSeconds(30) : TimeSpan.FromMilliseconds(100)
        }, new TtBaselineDefinition<TestDbContext>("startup-deadline-v1", TtSchemaInitialization.EnsureCreated),
            cancellation.Token,
            deleteHost: async container => { Interlocked.Increment(ref hostsDeleted); await container.DisposeAsync(); },
            beforeVerification: _ => { Interlocked.Increment(ref clones); return Task.CompletedTask; },
            prepareImage: async _ => { entered.TrySetResult(); await release.Task; });
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (callerCancellation) cancellation.Cancel();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(callerCancellation ? "StartupCanceled" : "StartupTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.NotEqual(Guid.Empty, cleanup.OwnerId);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal((0, 0), (hostsDeleted, clones));
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal((0, 0), (hostsDeleted, clones));
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(async () => { await using var fixture = await initializing; });
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
        }
    }
}

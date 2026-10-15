using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class DiLateCleanupTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Provider_cleanup_reports_a_late_startup_callback_failure_before_confirming_completion()
    {
        var prefix = $"tt-di-late-failure-{Guid.NewGuid():N}";
        var seeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSeed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        CancellationTokenRegistration callback = default;
        var calls = 0;
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        services.AddSingleton(new TtBaselineDefinition<TestDbContext>("di-late-callback-v1", TtSchemaInitialization.EnsureCreated,
            async (_, token) =>
            {
                callback = token.Register(() =>
                {
                    Assert.Equal(1, Interlocked.Increment(ref calls));
                    callbackEntered.TrySetResult();
                    releaseCallback.Wait();
                    throw new IOException("di-late-private-detail");
                });
                seeded.TrySetResult();
                await releaseSeed.Task;
            }));
        var provider = services.BuildServiceProvider();
        var owner = provider.GetRequiredService<ITtServiceFactory>();
        var initializing = owner.CreateFactoryAsync<TestDbContext>();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Task? closing = null;
        TtCleanup? cleanup = null;
        try
        {
            await seeded.Task.WaitAsync(TimeSpan.FromSeconds(45));
            var original = Assert.Single(await OwnedAsync()).ID;
            closing = provider.DisposeAsync().AsTask();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var initialFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("StartupCanceled", initialFailure.Stage);
            var drainFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", drainFailure.Stage);
            cleanup = Assert.IsType<TtCleanup>(drainFailure.Cleanup);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            releaseSeed.TrySetResult();
            Assert.False(pending.IsCompleted);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            releaseCallback.Set();
            var late = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal("StartupCancellation", late.Stage);
            Assert.DoesNotContain("di-late-private-detail", late.ToString());
            Assert.Same(cleanup, late.Cleanup);
            Assert.Equal(1, calls);
            Assert.Empty(await OwnedAsync());
            await cleanup.RetryCleanupAsync();
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            releaseSeed.TrySetResult();
            releaseCallback.Set();
            await Record.ExceptionAsync(() => initializing);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            var failure = await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
            callback.Dispose();
            await provider.DisposeAsync();
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

using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.DependencyInjection;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;
using OtherContext = TurboTest.Tests.DiStartupDrainTests.OtherBlockingContext;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class DiImmediateClosureTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_disposal_caller_closes_returned_handles_before_returning_the_shared_job()
    {
        var prefix = $"tt-di-peer-close-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.AutoMigrate = false;
            options.ContainerPrefix = prefix;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        var provider = services.BuildServiceProvider();
        var service = Assert.IsType<TtServiceFactory>(provider.GetRequiredService<ITtServiceFactory>());
        var existing = await service.CreateFactoryAsync<TestDbContext>();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var first = Task.Run(async () => await service.DisposeAsync(() =>
        {
            published.TrySetResult();
            release.Wait();
        }));
        TtCleanup? cleanup = null;
        try
        {
            await published.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var peer = service.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => existing.CreateLeaseAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            release.Set();
            var failure = await Record.ExceptionAsync(() => peer);
            cleanup = (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            Assert.Equal(0, existing.PoolState.Reserved);
        }
        finally
        {
            release.Set();
            var failure = await Record.ExceptionAsync(() => first);
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask());
            await Tt.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Provider_closure_rejects_existing_factory_acquisition_before_a_pending_initializer_returns()
    {
        var prefix = $"tt-di-close-now-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.AutoMigrate = false;
            options.ContainerPrefix = prefix;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        // A user-supplied registration is noncooperative before startup observes
        // its token. Holding it must not keep an existing fixture open.
        services.AddSingleton<TtBaselineDefinition<OtherContext>>(_ =>
        {
            entered.TrySetResult();
            release.Wait();
            return new TtBaselineDefinition<OtherContext>("held-di-registration-v1", TtSchemaInitialization.EnsureCreated);
        });
        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITtServiceFactory>();
        var existing = await service.CreateFactoryAsync<TestDbContext>();
        var initializing = Task.Run(() => service.CreateFactoryAsync<OtherContext>());
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Task? closing = null;
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(await OwnedAsync());
            closing = provider.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => existing.CreateLeaseAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.Set();
            var interrupted = await Record.ExceptionAsync(() => initializing);
            Assert.True(interrupted is TtLifecycleException or ObjectDisposedException);
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Empty(await OwnedAsync());
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            release.Set();
            await Record.ExceptionAsync(() => initializing);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            var failure = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            await Tt.DisposeAsync();
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

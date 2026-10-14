using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;
using TurboTest.EFCore.Extensions;
using BlockingContext = TurboTest.Tests.DrainDeadlineTests.BlockingContext;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class DiStartupDrainTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Provider_closes_all_fixtures_together_and_one_cleanup_capability_recovers_every_retained_host()
    {
        var prefix = $"tt-di-many-drain-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.AutoMigrate = false;
            options.DrainTimeout = TimeSpan.FromSeconds(1);
        });
        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITtServiceFactory>();
        var first = await service.CreateFactoryAsync<BlockingContext>();
        var other = await service.CreateFactoryAsync<OtherBlockingContext>();
        var a = await first.CreateLeaseAsync();
        var b = await other.CreateLeaseAsync();
        var firstContext = a.Context;
        var otherContext = b.Context;
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Task? closing = null;
        TtCleanup? cleanup = null;
        try
        {
            var originals = (await OwnedAsync()).Select(c => c.ID).Order().ToArray();
            Assert.Equal(2, originals.Length);
            closing = provider.DisposeAsync().AsTask();
            await firstContext.Closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await otherContext.Closing.Task.WaitAsync(TimeSpan.FromMilliseconds(400));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => first.CreateLeaseAsync());
            await Assert.ThrowsAsync<ObjectDisposedException>(() => other.CreateLeaseAsync());
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(originals, cleanup.OwnedResourceIds.Order());
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            firstContext.Release.TrySetResult();
            await a.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            otherContext.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Empty(await OwnedAsync());
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Equal((1, 1), (firstContext.Attempts, otherContext.Attempts));
        }
        finally
        {
            firstContext.Release.TrySetResult();
            otherContext.Release.TrySetResult();
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            var failure = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Provider_drain_preserves_a_live_owner_through_async_timeout_or_sync_disposal_rejection(bool synchronous)
    {
        var prefix = $"tt-di-live-drain-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.AutoMigrate = false;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        services.AddTurboTestDbContext<BlockingContext>();
        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITtServiceFactory>();
        var fixture = await service.CreateFactoryAsync<BlockingContext>();
        var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.InitializeTurboTestAsync<BlockingContext>();
        var lease = scope.ServiceProvider.GetRequiredService<TtLease<BlockingContext>>();
        var context = lease.Context;
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        try
        {
            var original = Assert.Single(await OwnedAsync()).ID;
            if (synchronous)
            {
                var rejection = Assert.Throws<InvalidOperationException>(() => provider.Dispose());
                Assert.Contains("DisposeAsync", rejection.Message);
                Assert.Equal(0, context.Attempts);
            }
            var closing = synchronous ? service.DisposeAsync().AsTask() : provider.DisposeAsync().AsTask();
            await context.Closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            Assert.Equal((1, 1), (fixture.PoolState.Reserved, context.Attempts));
            var pending = cleanup.RetryCleanupAsync();
            var returningScope = scope.DisposeAsync().AsTask();
            Assert.False(pending.IsCompleted);
            Assert.False(returningScope.IsCompleted);
            context.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await returningScope.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(await OwnedAsync());
            Assert.Equal((0, 1), (fixture.PoolState.Reserved, context.Attempts));
        }
        finally
        {
            context.Release.TrySetResult();
            var failure = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
            await scope.DisposeAsync();
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

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Provider_closure_interrupts_initialization_and_retains_pending_work_until_explicit_cleanup()
    {
        var prefix = $"tt-di-start-drain-{Guid.NewGuid():N}";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seedFinished = false;
        var services = new ServiceCollection();
        services.AddTurboTest(options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
        });
        services.AddSingleton(new TtBaselineDefinition<TestDbContext>("di-held-seed-v1", TtSchemaInitialization.EnsureCreated,
            async (_, _) => { entered.TrySetResult(); await release.Task; seedFinished = true; }));
        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITtServiceFactory>();
        var initializing = service.CreateFactoryAsync<TestDbContext>();
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        Task? closing = null;
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            var original = Assert.Single(await OwnedAsync()).ID;
            closing = provider.DisposeAsync().AsTask();
            var interrupted = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("StartupCanceled", interrupted.Stage);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(new[] { original }, interrupted.Cleanup!.OwnedResourceIds);
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            Assert.False(seedFinished);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => service.CreateFactoryAsync<TestDbContext>().WaitAsync(TimeSpan.FromSeconds(2)));
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(seedFinished);
            Assert.Empty(await OwnedAsync());
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Same(failure, await Record.ExceptionAsync(() => service.DisposeAsync().AsTask()));
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => initializing);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            var failure = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
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

    public sealed class OtherBlockingContext(DbContextOptions<OtherBlockingContext> options) : DbContext(options)
    {
        internal TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            Assert.Equal(1, ++Attempts);
            Closing.TrySetResult();
            await Release.Task;
            await base.DisposeAsync();
        }
    }
}

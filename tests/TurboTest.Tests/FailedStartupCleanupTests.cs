using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class FailedStartupCleanupTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Nested_startup_failures_keep_both_real_child_hosts_recoverable_after_outer_cleanup()
    {
        var prefix = $"tt-nested-startup-{Guid.NewGuid():N}";
        var holdChildren = true;
        var childDeletions = 0;
        var outerDeletions = 0;
        var known = new List<TtCleanup>();
        TtCleanup? returned = null;
        CancellationTokenRegistration registration = default;
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        try
        {
            var seed = await FailChildAsync("seed", "SELECT 1 / 0", "22012");
            var callback = await FailChildAsync("callback", "SELECT * FROM private_callback_missing", "42P01");
            var childIds = known.SelectMany(cleanup => cleanup.OwnedResourceIds).Order().ToArray();
            Assert.Equal(2, childIds.Distinct().Count());
            var definition = new TtBaselineDefinition<TestDbContext>("nested-outer", TtSchemaInitialization.EnsureCreated,
                (_, token) =>
                {
                    registration = token.Register(() => throw callback);
                    throw seed;
                });
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.CreatePreparedFactoryAsync(
                new TtOptions { PoolSize = 1, ContainerPrefix = prefix + "-outer" }, definition, default,
                deleteHost: async container =>
                {
                    if (Interlocked.Increment(ref outerDeletions) > 1)
                        throw new InvalidOperationException("Outer deletion was repeated.");
                    await container.DisposeAsync();
                }).WaitAsync(TimeSpan.FromSeconds(45)));
            returned = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(("BaselineInitialize", "22012"), (failure.Stage, failure.SqlState));
            Assert.Contains("22012", failure.Message);
            Assert.Contains("42P01", failure.Message);
            Assert.DoesNotContain("private", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.Equal(childIds, returned.OwnedResourceIds.Order().ToArray());
            Assert.Equal(childIds, (await OwnedAsync()).Select(container => container.ID).Order().ToArray());
            Assert.Equal((1, 0), (outerDeletions, childDeletions));
            holdChildren = false;
            await returned.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await returned.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(returned.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
            Assert.Equal((1, 2), (outerDeletions, childDeletions));
        }
        finally
        {
            holdChildren = false;
            registration.Dispose();
            foreach (var cleanup in known.Append(returned).OfType<TtCleanup>())
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(30)));
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            Assert.Empty(await OwnedAsync());
        }

        async Task<TtLifecycleException> FailChildAsync(string suffix, string sql, string expectedState)
        {
            var definition = new TtBaselineDefinition<TestDbContext>(suffix, TtSchemaInitialization.EnsureCreated,
                (context, _) => context.Database.ExecuteSqlRawAsync(sql));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.CreatePreparedFactoryAsync(
                new TtOptions { PoolSize = 1, ContainerPrefix = prefix + "-" + suffix }, definition, default,
                deleteHost: async container =>
                {
                    if (holdChildren) throw new IOException("private-held-child");
                    if (Interlocked.Increment(ref childDeletions) > 2)
                        throw new InvalidOperationException("Confirmed child deletion was repeated.");
                    await container.DisposeAsync();
                }).WaitAsync(TimeSpan.FromSeconds(45)));
            var cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            known.Add(cleanup);
            Assert.Equal(expectedState, failure.SqlState);
            Assert.Single(cleanup.OwnedResourceIds);
            return failure;
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
    public async Task A_blocking_startup_cancellation_callback_keeps_its_host_owned_without_blocking_the_failure_observer(bool initializationFailure)
    {
        var prefix = $"tt-blocked-startup-{Guid.NewGuid():N}";
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;
        var callbacks = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("blocked-startup-v1", TtSchemaInitialization.EnsureCreated,
            (_, token) =>
            {
                registration = token.Register(() =>
                {
                    if (Interlocked.Increment(ref callbacks) > 1) throw new InvalidOperationException("Cancellation was repeated.");
                    entered.TrySetResult();
                    release.Wait();
                });
                if (initializationFailure) throw new IOException("private-seed-detail");
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var initializing = Tt.CreateFactoryAsync(definition, options =>
        {
            options.PoolSize = 1;
            options.ContainerPrefix = prefix;
            options.DrainTimeout = TimeSpan.FromMilliseconds(100);
            options.StartupTimeout = TimeSpan.FromSeconds(30);
        });
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(initializationFailure ? "BaselineInitialize" : "StartupTimeout", failure.Stage);
            Assert.DoesNotContain("private-seed-detail", failure.ToString());
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            var original = Assert.Single(await OwnedAsync()).ID;
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
            Assert.Equal(1, callbacks);
        }
        finally
        {
            release.Set();
            registration.Dispose();
            var failure = await Record.ExceptionAsync(() => initializing);
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
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
    public async Task Throwing_startup_cancellation_callbacks_cannot_replace_the_initializer_error_or_lose_cleanup(bool failDelete)
    {
        var prefix = $"tt-startup-callback-{Guid.NewGuid():N}";
        CancellationTokenRegistration registration = default;
        var callbacks = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("callback-failure-v1", TtSchemaInitialization.EnsureCreated,
            (_, token) =>
            {
                registration = token.Register(() =>
                {
                    Interlocked.Increment(ref callbacks);
                    throw new IOException("private-cancellation-detail");
                });
                throw new InvalidOperationException("private-seed-detail");
            });
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        TtCleanup? cleanup = null;
        try
        {
            var failure = await Record.ExceptionAsync(() => Tt.CreatePreparedFactoryAsync(
                new TtOptions { PoolSize = 1, ContainerPrefix = prefix }, definition, default,
                deleteHost: async container =>
                {
                    if (failDelete) throw new IOException("private-delete-detail");
                    await container.DisposeAsync();
                }));
            var lifecycle = Assert.IsType<TtLifecycleException>(failure);
            Assert.Equal("BaselineInitialize", lifecycle.Stage);
            Assert.Contains(nameof(InvalidOperationException), lifecycle.Message);
            Assert.DoesNotContain("private-", lifecycle.ToString());
            if (failDelete)
            {
                cleanup = Assert.IsType<TtCleanup>(lifecycle.Cleanup);
                var original = Assert.Single(await OwnedAsync()).ID;
                Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
                var retryFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => cleanup.RetryCleanupAsync());
                Assert.Contains("SlotDispose", retryFailure.Message);
                Assert.Contains("StartupCancellation", retryFailure.Message);
                Assert.Contains(nameof(IOException), retryFailure.Message);
                Assert.DoesNotContain("private-", retryFailure.ToString());
                Assert.Null(retryFailure.InnerException);
                Assert.Same(cleanup, retryFailure.Cleanup);
                Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
                failDelete = false;
                await cleanup.RetryCleanupAsync();
                Assert.Empty(cleanup.OwnedResourceIds);
            }
            else
            {
                Assert.Null(lifecycle.Cleanup);
                Assert.Contains(nameof(AggregateException), lifecycle.Message);
            }
            Assert.Equal(1, callbacks);
            Assert.Empty(await OwnedAsync());
        }
        finally
        {
            registration.Dispose();
            failDelete = false;
            if (cleanup is not null) await cleanup.RetryCleanupAsync();
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

using Docker.DotNet;
using Docker.DotNet.Models;
using TurboTest.EFCore;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StartupCancellationOwnershipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "PostgreSQL")]
    public Task Interruption_owns_noncooperative_initialization_and_cancellation_until_both_settle(
        bool callerCancellation, bool lateFailure) => RunAsync(callerCancellation, lateFailure);

    // The RED for this case runs alone: the old timed cancellation source can
    // throw on the timer thread and terminate its test host.
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public Task Deadline_triggered_throwing_callback_is_reported_by_owned_cleanup()
        => RunAsync(callerCancellation: false, lateFailure: true);

    private static async Task RunAsync(bool callerCancellation, bool lateFailure)
    {
        var prefix = $"tt-startup-ownership-{Guid.NewGuid():N}";
        var seedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seedFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSeed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        using var caller = new CancellationTokenSource();
        CancellationTokenRegistration registration = default;
        var callbacks = 0;
        var deletions = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("owned-cancellation-v1", TtSchemaInitialization.EnsureCreated,
            async (_, token) =>
            {
                registration = token.Register(() =>
                {
                    if (Interlocked.Increment(ref callbacks) > 1)
                        throw new InvalidOperationException("Cancellation callback was repeated.");
                    callbackEntered.TrySetResult();
                    try
                    {
                        releaseCallback.Wait();
                        if (lateFailure) throw new IOException("startup-callback-private-detail");
                    }
                    finally { callbackFinished.TrySetResult(); }
                });
                seedEntered.TrySetResult();
                // Deliberately ignores the token; this job must remain owned.
                await releaseSeed.Task;
                seedFinished.TrySetResult();
            });
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var initializing = Tt.CreatePreparedFactoryAsync(new TtOptions
        {
            PoolSize = 1, ContainerPrefix = prefix, StartupTimeout = TimeSpan.FromSeconds(30)
        }, definition, caller.Token, deleteHost: async container =>
        {
            Assert.True(seedFinished.Task.IsCompleted, "Deletion began while initialization was active.");
            Assert.True(callbackFinished.Task.IsCompleted, "Deletion began while cancellation callback was active.");
            Assert.Equal(1, Interlocked.Increment(ref deletions));
            await container.DisposeAsync();
        });
        Task cancelCaller = Task.CompletedTask;
        TtCleanup? cleanup = null;
        try
        {
            await seedEntered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            if (callerCancellation) cancelCaller = caller.CancelAsync();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(45));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => initializing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(callerCancellation ? "StartupCanceled" : "StartupTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            var original = Assert.Single(await OwnedAsync()).ID;
            Assert.Equal(new[] { original }, cleanup.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            releaseSeed.TrySetResult();
            await seedFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, deletions);
            Assert.Equal(original, Assert.Single(await OwnedAsync()).ID);
            releaseCallback.Set();
            await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (lateFailure)
            {
                var callbackFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.Equal("StartupCancellation", callbackFailure.Stage);
                Assert.DoesNotContain("startup-callback-private-detail", callbackFailure.ToString());
                Assert.Empty(await OwnedAsync());
                await cleanup.RetryCleanupAsync();
            }
            else await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal((1, 1), (callbacks, deletions));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Empty(await OwnedAsync());
        }
        finally
        {
            releaseSeed.TrySetResult();
            releaseCallback.Set();
            await Record.ExceptionAsync(() => cancelCaller);
            registration.Dispose();
            var failure = await Record.ExceptionAsync(() => initializing);
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
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

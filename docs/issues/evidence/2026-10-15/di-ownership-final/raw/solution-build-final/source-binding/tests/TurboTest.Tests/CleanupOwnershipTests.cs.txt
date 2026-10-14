using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class CleanupOwnershipTests
{
    [Fact]
    public async Task Global_cleanup_bounds_its_wait_and_keeps_pending_work_for_a_later_drain()
    {
        await Tt.DisposeAsync();
        Tt.Configure(options => options.DrainTimeout = TimeSpan.FromMilliseconds(100));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var resources = new List<string> { "pending-owned-resource" };
        var cleanup = TtCleanupRegistry.Retain(Guid.NewGuid(), async () =>
        {
            if (++calls > 1) throw new InvalidOperationException("Pending cleanup was restarted.");
            started.TrySetResult();
            await release.Task;
            resources.Clear();
        }, () => resources);
        var otherCompleted = false;
        var other = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            otherCompleted = true;
            return Task.CompletedTask;
        }, () => ["other-owned-resource"]);
        try
        {
            var draining = Tt.DisposeAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => draining.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", error.Stage);
            Assert.Same(cleanup, error.Cleanup);
            Assert.Equal(["pending-owned-resource"], cleanup.OwnedResourceIds);
            Assert.True(otherCompleted);
            Assert.Empty(other.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            await Tt.DisposeAsync();
            Assert.Equal(1, calls);
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            release.TrySetResult();
            await cleanup.RetryCleanupAsync();
            await other.RetryCleanupAsync();
            await Tt.DisposeAsync();
            Tt.Configure(options => options.DrainTimeout = TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Reentrant_cleanup_observes_the_same_pending_attempt_without_repeating_the_callback()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task? nested = null;
        TtCleanup? cleanup = null;
        cleanup = new TtCleanup(Guid.NewGuid(), async () =>
        {
            if (++calls > 1) throw new InvalidOperationException("Cleanup callback was repeated.");
            nested = cleanup!.RetryCleanupAsync();
            entered.TrySetResult();
            await release.Task;
        }, () => ["held-resource"]);
        var attempt = cleanup.RetryCleanupAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(attempt, nested);
            Assert.False(attempt.IsCompleted);
            Assert.Equal(["held-resource"], cleanup.OwnedResourceIds);
        }
        finally { release.TrySetResult(); }
        await attempt.WaitAsync(TimeSpan.FromSeconds(2));
        await cleanup.RetryCleanupAsync();
        Assert.Equal(1, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
    }

    [Fact]
    public async Task Global_cleanup_continues_after_one_failure_and_retains_only_the_failed_owner()
    {
        var fail = true;
        var retained = new List<string> { "first-owned-resource" };
        var other = new List<string> { "second-owned-resource" };
        var first = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            if (fail) throw new IOException("private-delete-detail");
            retained.Clear();
            return Task.CompletedTask;
        }, () => retained);
        var second = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            other.Clear();
            return Task.CompletedTask;
        }, () => other);
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.DisposeAsync());
            Assert.Same(first, error.Cleanup);
            Assert.DoesNotContain("private-delete-detail", error.ToString());
            Assert.Equal(["first-owned-resource"], first.OwnedResourceIds);
            Assert.Empty(second.OwnedResourceIds);
            fail = false;
            await Tt.DisposeAsync();
            Assert.Empty(first.OwnedResourceIds);
        }
        finally
        {
            fail = false;
            await first.RetryCleanupAsync();
            await second.RetryCleanupAsync();
        }
    }

    [Fact]
    public async Task Concurrent_cleanup_shares_one_attempt_and_retains_accounting_until_completion()
    {
        var resources = new List<string> { "resource-first", "resource-second" };
        var permit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cleanup = new TtCleanup(Guid.NewGuid(), async () =>
        {
            calls++;
            started.SetResult();
            await permit.Task;
            resources.Clear();
        }, () => resources);
        var snapshot = cleanup.OwnedResourceIds;
        var first = cleanup.RetryCleanupAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var concurrent = cleanup.RetryCleanupAsync();
        try
        {
            Assert.Same(first, concurrent);
            Assert.False(first.IsCompleted);
            Assert.Equal(["resource-first", "resource-second"], cleanup.OwnedResourceIds);
        }
        finally { permit.TrySetResult(); }
        await Task.WhenAll(first, concurrent).WaitAsync(TimeSpan.FromSeconds(2));
        await cleanup.RetryCleanupAsync();
        Assert.Equal(1, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
        Assert.Equal(["resource-first", "resource-second"], snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_retry_keeps_safe_diagnostics_and_the_capability_for_another_attempt(bool lifecycle)
    {
        var fail = true;
        var calls = 0;
        var resources = new List<string> { "retained-resource" };
        var cleanup = new TtCleanup(Guid.NewGuid(), () =>
        {
            calls++;
            if (fail)
            {
                var cause = new IOException("cleanup-secret");
                throw lifecycle ? new TtLifecycleException("OwnedHostDelete", cause) : cause;
            }
            resources.Clear();
            return Task.CompletedTask;
        }, () => resources);
        var error = await Assert.ThrowsAsync<TtLifecycleException>(() => cleanup.RetryCleanupAsync());
        Assert.Equal(lifecycle ? "OwnedHostDelete" : "FailedStartupCleanup", error.Stage);
        Assert.DoesNotContain("cleanup-secret", error.ToString());
        Assert.Same(cleanup, error.Cleanup);
        var supplemented = error.WithCleanupFailure(new IOException("another-secret"));
        Assert.Same(cleanup, supplemented.Cleanup);
        Assert.DoesNotContain("secret", supplemented.ToString());
        Assert.Equal(["retained-resource"], cleanup.OwnedResourceIds);
        fail = false;
        await cleanup.RetryCleanupAsync();
        await cleanup.RetryCleanupAsync();
        Assert.Equal(2, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
    }
}

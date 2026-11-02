using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class BackgroundReturnOwnershipTests
{
    [Fact]
    public async Task Owner_cancellation_retires_an_executing_reset_and_releases_its_permit_for_cleanup()
    {
        var executing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returns = new TtBackgroundReturns();
        var deleted = 0;
        var pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ => { Assert.Equal(1, ++deleted); return Task.CompletedTask; });
        var lease = await pool.AcquireAsync();
        returns.Start(() => pool.ReturnAsync(lease, async (_, token) =>
        {
            executing.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        }, backgroundReturn: true));
        await executing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, pool.Snapshot.ExecutingResets);
        pool.StopAcceptingWork();
        var drainedReturns = returns.DrainAsync();
        await pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await drainedReturns.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(returns.TakeFailures());
        Assert.Equal((1, 0, 0), (deleted, pool.Snapshot.Reserved, pool.Snapshot.ExecutingResets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stop_policy_rejects_demand_before_compensation_can_publish_a_replacement(bool stop)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0; var deletions = 0;
        var returns = new TtBackgroundReturns();
        await using var pool = new TtSlotPool<object>(1, 1, _ =>
        {
            Assert.True(++created <= 2, "Recovery exceeded its expected replacement count.");
            return new object();
        }, (_, _) => Task.CompletedTask, async _ =>
        {
            Assert.True(++deletions <= 2, "Cleanup repeated deletion.");
            if (deletions == 1) { deleting.SetResult(); await release.Task; }
        });
        var original = await pool.AcquireAsync();
        var waiting = pool.AcquireAsync();
        returns.Start(() => pool.ReturnAsync(original, (_, _) => Task.FromException<bool>(new IOException("private-failure")),
            backgroundReturn: true, stopOnFailure: stop));
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (stop)
            {
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains("IOException", failure.Message);
                Assert.Same(failure, await Assert.ThrowsAsync<TtLifecycleException>(() => pool.AcquireAsync()));
            }
            else Assert.False(waiting.IsCompleted);
            release.SetResult();
            await returns.DrainAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains("IOException", Assert.Single(returns.TakeFailures()).Message);
            if (!stop)
            {
                var recovered = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.NotSame(original.Value, recovered.Value);
                await pool.ReturnAsync(recovered, (_, _) => Task.CompletedTask);
            }
            Assert.Equal(stop ? 1 : 2, created);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Drain_owns_reentrant_and_delayed_returns_until_all_safe_failures_are_recorded()
    {
        var owner = new TtBackgroundReturns();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.Start(() => Task.CompletedTask);
        owner.Start(() => throw new IOException("private-inline"));
        owner.Start(async () => { await release.Task; throw new UnauthorizedAccessException("private-late"); });
        Task? nested = null;
        owner.Start(() =>
        {
            nested = owner.DrainAsync();
            Assert.False(nested.IsCompleted);
            return Task.CompletedTask;
        });
        Assert.Same(nested, owner.DrainAsync());
        Assert.Throws<ObjectDisposedException>(() => owner.Start(() => Task.CompletedTask));
        Assert.False(nested!.IsCompleted);
        release.SetResult();
        await nested.WaitAsync(TimeSpan.FromSeconds(2));
        var failures = owner.TakeFailures();
        Assert.Collection(failures,
            error => Assert.Contains("IOException", error.Message),
            error => Assert.Contains("UnauthorizedAccessException", error.Message));
        Assert.All(failures, error =>
        {
            Assert.Equal("BackgroundReset", error.Stage);
            Assert.DoesNotContain("private-", error.ToString());
            Assert.Null(error.InnerException);
        });
        Assert.Empty(owner.TakeFailures());
    }

    [Fact]
    public async Task Closing_cancels_a_reset_waiting_behind_maintenance_without_inventing_a_reset_failure()
    {
        var parking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new HashSet<object>();
        var returns = new TtBackgroundReturns();
        var pool = new TtSlotPool<object>(2, 1, _ => new object(), (_, _) => Task.CompletedTask,
            resource => { Assert.True(deleted.Add(resource)); return Task.CompletedTask; });
        var idle = await pool.AcquireAsync();
        var leased = await pool.AcquireAsync();
        await pool.ReturnAsync(idle, (_, _) => Task.CompletedTask);
        var maintenance = pool.ParkIdleAsync(0, async (_, token) =>
        {
            parking.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await parking.Task.WaitAsync(TimeSpan.FromSeconds(2));
        returns.Start(() => pool.ReturnAsync(leased,
            (_, _) => throw new InvalidOperationException("A queued reset ran during shutdown."), backgroundReturn: true));
        Assert.Equal(1, pool.Snapshot.WaitingResets);
        pool.StopAcceptingWork();
        var completeReturns = returns.DrainAsync();
        await pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await completeReturns.WaitAsync(TimeSpan.FromSeconds(2));
        await Record.ExceptionAsync(() => maintenance);
        Assert.Empty(returns.TakeFailures());
        Assert.Equal(2, deleted.Count);
        Assert.Equal(0, pool.Snapshot.Reserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_material_reset_failure_during_shutdown_is_retained_after_physical_cleanup(bool unrelatedCancellation)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returns = new TtBackgroundReturns();
        var deleted = 0;
        var pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ => { Assert.Equal(1, ++deleted); return Task.CompletedTask; });
        var lease = await pool.AcquireAsync();
        async Task<bool> FailReset(object resource, CancellationToken token)
        {
            await release.Task;
            if (unrelatedCancellation) throw new OperationCanceledException("private-other-cancellation", new CancellationToken(true));
            throw new IOException("private-shutdown-fault");
        }
        returns.Start(() => pool.ReturnAsync(lease, FailReset, backgroundReturn: true));
        pool.StopAcceptingWork();
        var completeReturns = returns.DrainAsync();
        var drain = pool.DisposeAsync().AsTask();
        Assert.False(drain.IsCompleted);
        release.SetResult();
        await Task.WhenAll(drain, completeReturns).WaitAsync(TimeSpan.FromSeconds(2));
        var failure = Assert.Single(returns.TakeFailures());
        Assert.Contains(unrelatedCancellation ? "OperationCanceledException" : "IOException", failure.Message);
        Assert.DoesNotContain("private-", failure.Message);
        Assert.Equal((1, 0), (deleted, pool.Snapshot.Reserved));
    }
}

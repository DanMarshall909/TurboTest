using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotRewarmDrainTests
{
    [Fact]
    public async Task A_rewarm_failure_delivered_by_exhausted_cleanup_is_not_reported_again_at_drain()
    {
        var attempts = 0;
        var allowCleanup = false;
        var confirmed = 0;
        await using var owner = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ =>
            {
                if (++attempts > 4) throw new InvalidOperationException("Cleanup exceeded its finite oracle.");
                if (!allowCleanup) throw new UnauthorizedAccessException("private-cleanup-failure");
                confirmed++;
                return Task.CompletedTask;
            }, rewarm: (_, _) => Task.FromException(new IOException("private-rewarm-failure")));
        var initial = await owner.AcquireAsync();
        await owner.ReturnAsync(initial, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("IOException", failure.Message);
            Assert.Contains("UnauthorizedAccessException", failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Equal((3, 1, 0), (attempts, owner.Snapshot.Quarantined, confirmed));
            allowCleanup = true;
            await owner.DisposeAsync();
            Assert.Equal((4, 0, 1), (attempts, owner.Snapshot.Reserved, confirmed));
            await owner.RetryCleanupAsync();
            Assert.Equal(4, attempts);
        }
        finally { allowCleanup = true; }
    }

    [Fact]
    public async Task Shutdown_canceling_the_retry_delay_retains_the_unseen_rewarm_failure()
    {
        var clock = new HeldRecoveryClock();
        var attempts = 0;
        var confirmed = 0;
        var owner = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ =>
            {
                if (++attempts > 2) throw new InvalidOperationException("Cleanup exceeded its finite oracle.");
                if (attempts == 1) throw new UnauthorizedAccessException("private-first-deletion");
                confirmed++;
                return Task.CompletedTask;
            }, rewarm: (_, _) => Task.FromException(new IOException("private-unseen-rewarm")), timeProvider: clock);
        var initial = await owner.AcquireAsync();
        await owner.ReturnAsync(initial, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        var waiting = owner.AcquireAsync();
        Assert.True(clock.Requested);
        Assert.Equal((1, 1), (attempts, owner.Snapshot.Quarantined));
        var closing = owner.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing);
            Assert.Contains("IOException", failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Equal((0, 2, 1), (owner.Snapshot.Reserved, attempts, confirmed));
            await owner.RetryCleanupAsync();
            Assert.Equal(2, attempts);
        }
        finally
        {
            await Record.ExceptionAsync(() => waiting);
            await Record.ExceptionAsync(() => closing);
            await owner.RetryCleanupAsync();
        }
    }

    private sealed class HeldRecoveryClock : TimeProvider
    {
        internal bool Requested { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Requested = true;
            return new HeldTimer();
        }

        private sealed class HeldTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closure_during_recovery_retains_the_live_rewarm_failure_through_confirmed_deletion(bool retry)
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creates = 0;
        var attempts = 0;
        var confirmed = 0;
        var owner = new TtSlotPool<object>(1, 1, _ =>
        {
            if (++creates > 1) throw new InvalidOperationException("Shutdown started a replacement.");
            return new object();
        }, (_, _) => Task.CompletedTask, async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt > (retry ? 2 : 1)) throw new InvalidOperationException("Confirmed deletion was repeated.");
            if (retry && attempt == 1) throw new UnauthorizedAccessException("private-unconfirmed-delete");
            deleting.TrySetResult();
            await release.Task;
            Interlocked.Increment(ref confirmed);
        }, rewarm: (_, _) => Task.FromException(new IOException("private-live-rewarm-failure")));
        var initial = await owner.AcquireAsync();
        await owner.ReturnAsync(initial, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        var waiting = owner.AcquireAsync();
        await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = owner.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            Assert.False(closing.IsCompleted);
            Assert.Equal((1, 1, 0), (creates, owner.Snapshot.Reserved, confirmed));
            release.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("IOException", failure.Message);
            Assert.DoesNotContain("private-live-rewarm-failure", failure.ToString());
            Assert.DoesNotContain("private-unconfirmed-delete", failure.ToString());
            Assert.Equal((0, 1, 1, retry ? 2 : 1), (owner.Snapshot.Reserved, creates, confirmed, attempts));
            await owner.RetryCleanupAsync();
            await owner.RetryCleanupAsync();
            Assert.Equal(retry ? 2 : 1, attempts);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => waiting);
            await Record.ExceptionAsync(() => closing);
            await owner.RetryCleanupAsync();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Closure_joins_rewarm_and_reports_material_late_failure_once_without_replacement(int outcome)
    {
        var verifying = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var creates = 0;
        var deletes = 0;
        var owner = new TtSlotPool<object>(1, 1, _ =>
        {
            if (++creates > 1) throw new InvalidOperationException("Closure created a replacement.");
            return new object();
        }, (_, _) => Task.CompletedTask, _ =>
        {
            if (++deletes > 1) throw new InvalidOperationException("Closure deleted the resource twice.");
            return Task.CompletedTask;
        }, rewarm: async (_, token) =>
        {
            verifying.TrySetResult();
            await release.Task;
            if (outcome == 1) token.ThrowIfCancellationRequested();
            if (outcome == 2) throw new IOException("private-late-rewarm-payload");
        });
        var initial = await owner.AcquireAsync();
        await owner.ReturnAsync(initial, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        var acquisition = owner.AcquireAsync();
        await verifying.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = owner.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => acquisition);
            Assert.False(closing.IsCompleted);
            Assert.Equal((1, 1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Rewarming, creates, deletes));
            release.TrySetResult();
            if (outcome == 2)
            {
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains("IOException", failure.Message);
                Assert.DoesNotContain("private-late-rewarm-payload", failure.ToString());
                await owner.RetryCleanupAsync();
            }
            else await closing.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((0, 1, 1), (owner.Snapshot.Reserved, creates, deletes));
            await owner.RetryCleanupAsync();
            Assert.Equal(1, deletes);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => acquisition);
            await Record.ExceptionAsync(() => closing);
            await owner.RetryCleanupAsync();
        }
    }
}

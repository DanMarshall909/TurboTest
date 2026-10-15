using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotIdleRecoveryTests
{
    private sealed record Resource(Guid Id);

    [Fact]
    public async Task Failed_rewarm_releases_preparation_before_waiting_for_cleanup_held_by_replacement()
    {
        var rewarming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failRewarm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifyReplacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        await using var owner = new TtSlotPool<Resource>(2, 1, _ =>
        {
            if (Interlocked.Increment(ref created) > 4) throw new InvalidOperationException("Two damaged slots exceeded their replacement budget.");
            return new Resource(Guid.NewGuid());
        }, async (_, _) =>
        {
            if (created == 3)
            {
                replacing.TrySetResult();
                await verifyReplacement.Task;
            }
            if (created == 4) recovered.TrySetResult();
        }, _ => Task.CompletedTask, rewarm: async (_, _) =>
        {
            rewarming.TrySetResult();
            await failRewarm.Task;
            throw new IOException("Rewarm validation failed.");
        });
        var retained = await owner.AcquireAsync();
        var changed = await owner.AcquireAsync();
        var originalChangedResource = changed.Value;
        await owner.ReturnAsync(retained, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        using var cancel = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancel.Token);
        await rewarming.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var returning = owner.ReturnAsync(changed, (_, _) => Task.FromResult(false));
        try
        {
            Assert.False(returning.IsCompleted);
            failRewarm.TrySetResult();
            await replacing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(waiting.IsCompleted);
            Assert.Equal(2, owner.Snapshot.Reserved);
            verifyReplacement.TrySetResult();
            await returning.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotSame(originalChangedResource, (await waiting.WaitAsync(TimeSpan.FromSeconds(2))).Value);
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(4, created);
        }
        finally
        {
            cancel.Cancel();
            failRewarm.TrySetResult();
            verifyReplacement.TrySetResult();
            await Record.ExceptionAsync(() => returning);
            await Record.ExceptionAsync(() => waiting);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_idle_work_keeps_capacity_through_unconfirmed_deletion_then_publishes_one_verified_replacement(bool rewarm)
    {
        var parkStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failPark = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var attempts = 0;
        var injectFailure = true;
        Resource? originalResource = null;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, _ =>
        {
            if (Interlocked.Increment(ref created) > 2) throw new InvalidOperationException("Idle recovery exceeded one replacement.");
            return new Resource(Guid.NewGuid());
        }, (_, _) => Task.CompletedTask, async resource =>
        {
            if (injectFailure && ReferenceEquals(resource, originalResource))
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt > 2) throw new InvalidOperationException("Confirmed idle deletion was retried.");
                if (attempt == 1) throw new IOException("Deletion has not been confirmed.");
                retryStarted.TrySetResult();
                await confirmDeletion.Task;
            }
            deleted.Add(resource.Id);
        }, rewarm: (_, _) => Task.FromException(new IOException("Retained validation failed.")));
        var original = await owner.AcquireAsync();
        originalResource = original.Value;
        await owner.ReturnAsync(original, (_, _) => Task.CompletedTask);
        var maintenance = owner.ParkIdleAsync(0, async (_, _) =>
        {
            if (rewarm) return;
            parkStarted.TrySetResult();
            await failPark.Task;
            throw new IOException("Pool closure failed.");
        });
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            if (rewarm) await maintenance;
            else await parkStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            waiting = owner.AcquireAsync(cancel.Token);
            if (!rewarm)
            {
                failPark.TrySetResult();
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => maintenance);
                Assert.Equal("SlotPark", failure.Stage);
            }
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1, 1, 0), (created, owner.Snapshot.Reserved, owner.Snapshot.Retiring, owner.Snapshot.Ready));
            Assert.Empty(deleted);
            confirmDeletion.TrySetResult();
            var replacement = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotSame(originalResource, replacement.Value);
            Assert.NotEqual(original.Id, replacement.Id);
            Assert.Equal(new[] { originalResource.Id }, deleted);
            Assert.Equal((2, 2, 1, 1), (created, attempts, owner.Snapshot.Reserved, owner.Snapshot.Leased));
        }
        finally
        {
            injectFailure = false;
            cancel.Cancel();
            failPark.TrySetResult();
            confirmDeletion.TrySetResult();
            await Record.ExceptionAsync(() => maintenance);
            if (waiting is not null) await Record.ExceptionAsync(() => waiting);
        }
    }
}

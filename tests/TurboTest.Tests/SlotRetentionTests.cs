using TurboTest.EFCore.Internal;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class SlotRetentionTests
{
    private sealed record Resource(Guid Id);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_retry_preserves_capacity_and_replaces_only_for_renewed_demand(bool renewed)
    {
        var retry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var attempts = 0;
        var live = true;
        var inject = true;
        Resource? original = null;
        await using var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            var count = Interlocked.Increment(ref created);
            if (count > 1 && (!renewed || live)) throw new InvalidOperationException("Retirement recreated without demand or before deletion.");
            if (count > 2) throw new InvalidOperationException("Retirement created more than one demanded replacement.");
            return new Resource(id);
        }, (_, _) => Task.CompletedTask, async resource =>
        {
            if (!inject || !ReferenceEquals(resource, original)) return;
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt > 2) throw new InvalidOperationException("Confirmed retirement was retried.");
            if (attempt == 1) throw new IOException("The idle drop has not been confirmed.");
            retry.TrySetResult();
            await confirm.Task;
            live = false;
        });
        var lease = await owner.AcquireAsync();
        original = lease.Value;
        await owner.ReturnAsync(lease, (_, _) => Task.CompletedTask);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask, maxParked: 0));
            Assert.Equal("SlotIdleRetire", failure.Stage);
            await retry.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.Retiring));
            if (renewed)
            {
                waiting = owner.AcquireAsync(cancel.Token);
                Assert.False(waiting.IsCompleted);
            }
            confirm.TrySetResult();
            if (waiting is not null)
            {
                var replacement = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.NotSame(original, replacement.Value);
                Assert.Equal((2, 1, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.Leased));
            }
            else
            {
                while (owner.Snapshot.Reserved != 0) await Task.Delay(1, cancel.Token);
                Assert.Equal((1, 0, 0), (created, owner.Snapshot.Reserved, owner.Snapshot.Ready));
            }
            Assert.Equal(2, attempts);
            Assert.False(live);
        }
        finally
        {
            inject = false;
            cancel.Cancel();
            confirm.TrySetResult();
            if (waiting is not null) await Record.ExceptionAsync(() => waiting);
        }
    }

    [Fact]
    public async Task Retained_limit_parks_one_surplus_and_holds_the_other_reservation_until_confirmed_deletion()
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parked = new List<Guid>();
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, async resource =>
            {
                deleting.TrySetResult();
                await confirm.Task;
                deleted.Add(resource.Id);
            });
        await owner.WarmAsync(3, default);
        var maintenance = owner.ParkIdleAsync(1, (resource, _) =>
        {
            parked.Add(resource.Id);
            return Task.CompletedTask;
        }, maxParked: 1);
        try
        {
            Assert.False(maintenance.IsCompleted);
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((3, 1, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked, owner.Snapshot.Retiring));
            Assert.Single(parked);
            Assert.Empty(deleted);
            confirm.TrySetResult();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((2, 1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked, owner.Snapshot.Retiring));
            Assert.Single(deleted);
            Assert.DoesNotContain(deleted[0], parked);
        }
        finally
        {
            confirm.TrySetResult();
            await maintenance;
        }
        await owner.DisposeAsync();
        Assert.Equal(3, deleted.Distinct().Count());
        Assert.Equal(0, owner.Snapshot.Reserved);
    }
}

using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotParkingTests
{
    [Fact]
    public async Task Repeated_maintenance_does_not_select_or_repeat_an_already_claimed_parking_job()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(2, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        var first = await owner.AcquireAsync();
        var second = await owner.AcquireAsync();
        await owner.ReturnAsync(first, (_, _) => Task.CompletedTask);
        await owner.ReturnAsync(second, (_, _) => Task.CompletedTask);
        async Task Park(Resource _, CancellationToken token)
        {
            if (Interlocked.Increment(ref callbacks) > 2) throw new InvalidOperationException("Parking exceeded its claim count.");
            entered.TrySetResult();
            await release.Task;
        }
        var pending = owner.ParkIdleAsync(0, Park);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await owner.ParkIdleAsync(0, Park);
            Assert.False(pending.IsCompleted);
            Assert.Equal((2, 2, 1), (owner.Snapshot.Reserved, owner.Snapshot.Parking, callbacks));
            Assert.Equal((1, 0), (owner.Snapshot.ExecutingCleanup, owner.Snapshot.ExecutingResets));
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            await owner.ParkIdleAsync(0, Park);
            Assert.Equal((2, 2, 0), (callbacks, owner.Snapshot.Parked, owner.Snapshot.Parking));
            Assert.Equal(0, owner.Snapshot.ExecutingCleanup);
            await owner.DisposeAsync();
            Assert.Equal(new[] { first.Value.Id, second.Value.Id }.Order(), deleted.Order());
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => pending);
        }
    }

    private sealed record Resource(Guid Id);

    [Fact]
    public async Task Demand_arriving_during_pool_closure_rewarms_after_parking_finishes()
    {
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rewarms = 0;
        await using var owner = new TtSlotPool<Resource>(1, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: (_, _) => { rewarms++; return Task.CompletedTask; });
        var original = await owner.AcquireAsync();
        await owner.ReturnAsync(original, (_, _) => Task.CompletedTask);
        var maintained = owner.ParkIdleAsync(0, async (_, _) =>
        {
            closing.TrySetResult();
            await release.Task;
        });
        await closing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancel = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancel.Token);
        try
        {
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Parking, rewarms));
            release.TrySetResult();
            await maintained;
            Assert.Same(original, await waiting.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal((1, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Leased, rewarms));
        }
        finally
        {
            cancel.Cancel();
            release.TrySetResult();
            await maintained;
            await Record.ExceptionAsync(() => waiting);
        }
    }

    [Fact]
    public async Task Concurrent_rewarms_share_the_preparation_limit_and_publish_only_verified_resources()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var rewarms = 0;
        await using var owner = new TtSlotPool<Resource>(2, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: async (_, _) =>
            {
                if (Interlocked.Increment(ref active) > 1) throw new InvalidOperationException("Rewarm exceeded the preparation limit.");
                Interlocked.Increment(ref rewarms);
                begun.TrySetResult();
                try { await release.Task; }
                finally { Interlocked.Decrement(ref active); }
            });
        await owner.WarmAsync(2, default);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        using var cancel = new CancellationTokenSource();
        var requests = new[] { owner.AcquireAsync(cancel.Token), owner.AcquireAsync(cancel.Token) };
        try
        {
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((2, 2, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Rewarming, active, rewarms));
            Assert.All(requests, request => Assert.False(request.IsCompleted));
            release.TrySetResult();
            var results = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, results.Select(result => result.Id).Distinct().Count());
            Assert.Equal((2, 2, 0, 2), (owner.Snapshot.Reserved, owner.Snapshot.Leased, active, rewarms));
        }
        finally
        {
            cancel.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(() => Task.WhenAll(requests));
        }
    }

    [Fact]
    public async Task Acquisition_rewarms_the_retained_reservation_at_capacity_and_waits_for_validation()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var prepared = 0;
        var rewarms = 0;
        await using var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            if (Interlocked.Increment(ref created) > 1) throw new InvalidOperationException("A retained reservation was replaced by a fresh clone.");
            return new Resource(id);
        }, (_, _) => { prepared++; return Task.CompletedTask; }, _ => Task.CompletedTask,
            rewarm: async (_, _) =>
            {
                Assert.Equal(1, Interlocked.Increment(ref rewarms));
                begun.TrySetResult();
                await release.Task;
            });
        var original = await owner.AcquireAsync();
        await owner.ReturnAsync(original, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        using var canceledObserver = new CancellationTokenSource();
        using var nextObserver = new CancellationTokenSource();
        var first = owner.AcquireAsync(canceledObserver.Token);
        Task<TtSlotPool<Resource>.Reservation>? second = null;
        try
        {
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1, 0, 0), (owner.Snapshot.Reserved, owner.Snapshot.Rewarming,
                owner.Snapshot.Ready, owner.Snapshot.Parked));
            Assert.False(first.IsCompleted);
            canceledObserver.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            second = owner.AcquireAsync(nextObserver.Token);
            Assert.False(second.IsCompleted);
            release.TrySetResult();
            var reused = await second.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(original, reused);
            Assert.Same(original.Value, reused.Value);
            Assert.Equal((1, 1, 1), (created, prepared, rewarms));
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Leased, owner.Snapshot.Rewarming));
        }
        finally
        {
            canceledObserver.Cancel();
            nextObserver.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(() => first);
            if (second is not null) await Record.ExceptionAsync(() => second);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Parking_preserves_the_configured_ready_floor(int floor)
    {
        var parked = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        await owner.WarmAsync(3, default);
        await owner.ParkIdleAsync(floor, (resource, _) =>
        {
            parked.Add(resource.Id);
            return Task.CompletedTask;
        });
        Assert.Equal((3, floor, 3 - floor), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked));
        Assert.Equal(3 - floor, parked.Distinct().Count());
        if (floor > 0)
        {
            var acquired = await owner.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain(acquired.Value.Id, parked);
            Assert.Equal(floor - 1, owner.Snapshot.Ready);
        }
    }

    [Fact]
    public async Task Parking_claims_only_idle_ready_slots_and_retains_capacity_until_shutdown()
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePark = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var parked = new List<Guid>();
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(4, 4, id => new Resource(id), async (_, _) =>
        {
            if (Interlocked.Increment(ref created) == 4)
            {
                preparing.TrySetResult();
                await releasePreparation.Task;
            }
        }, resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        var leased = await owner.AcquireAsync();
        var returning = await owner.AcquireAsync();
        var idle = await owner.AcquireAsync();
        using var cancel = new CancellationTokenSource();
        var pending = owner.AcquireAsync(cancel.Token);
        Task? returned = null;
        Task? maintained = null;
        try
        {
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            await owner.ReturnAsync(idle, (_, _) => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(2));
            returned = owner.ReturnAsync(returning, async (_, _) =>
            {
                resetting.TrySetResult();
                await releaseReset.Task;
            });
            await resetting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((4, 1, 1, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Preparing,
                owner.Snapshot.Leased, owner.Snapshot.ExecutingResets, owner.Snapshot.Ready));
            maintained = owner.ParkIdleAsync(0, async (resource, _) =>
            {
                parked.Add(resource.Id);
                parking.TrySetResult();
                await releasePark.Task;
            });
            // Parking uses the reset/cleanup limit and cannot bypass held reset.
            Assert.False(maintained.IsCompleted);
            Assert.Empty(parked);
            Assert.Equal((4, 0, 0, 1), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked, owner.Snapshot.Parking));
            releaseReset.TrySetResult();
            await returned;
            await parking.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { idle.Value.Id }, parked);
            Assert.DoesNotContain(leased.Value.Id, parked);
            Assert.False(maintained.IsCompleted);
            releasePark.TrySetResult();
            await maintained.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((4, 1), (owner.Snapshot.Reserved, owner.Snapshot.Parked));
            Assert.Empty(deleted);
        }
        finally
        {
            cancel.Cancel();
            releaseReset.TrySetResult();
            releasePark.TrySetResult();
            releasePreparation.TrySetResult();
            if (returned is not null) await returned;
            if (maintained is not null) await maintained;
            await Record.ExceptionAsync(() => pending);
        }
        await owner.DisposeAsync();
        Assert.Equal(4, deleted.Distinct().Count());
        Assert.Equal(0, owner.Snapshot.Reserved);
    }
}

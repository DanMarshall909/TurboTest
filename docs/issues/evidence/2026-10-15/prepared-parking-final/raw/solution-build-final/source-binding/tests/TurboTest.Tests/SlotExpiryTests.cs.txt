using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotExpiryTests
{
    private sealed record Resource(Guid Id);

    [Fact]
    public async Task Verified_return_renews_idle_age_and_expiry_does_not_select_a_leased_slot()
    {
        var clock = new ManualPoolClock();
        var resources = new List<Resource>();
        var parked = new List<Guid>();
        var deleted = new List<Guid>();
        var lifetime = TimeSpan.FromSeconds(10);
        await using var owner = new TtSlotPool<Resource>(3, 1, id =>
        {
            var resource = new Resource(id);
            resources.Add(resource);
            return resource;
        }, (_, _) => Task.CompletedTask, resource => { deleted.Add(resource.Id); return Task.CompletedTask; }, timeProvider: clock);
        await owner.WarmAsync(3, default);
        clock.Advance(lifetime);
        var leased = await owner.AcquireAsync();
        var renewed = await owner.AcquireAsync();
        await owner.ReturnAsync(renewed, (_, _) => Task.CompletedTask);
        await owner.ParkIdleAsync(0, (resource, _) => { parked.Add(resource.Id); return Task.CompletedTask; }, idleLifetime: lifetime);
        Assert.Equal(new[] { renewed.Value.Id }, parked);
        Assert.Equal(new[] { resources.Single(resource => resource.Id != leased.Value.Id && resource.Id != renewed.Value.Id).Id }, deleted);
        Assert.Equal((2, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Leased, owner.Snapshot.Parked));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Expired_ready_slots_retire_above_the_protected_ready_floor(int floor)
    {
        var clock = new ManualPoolClock();
        var deleted = new List<Guid>();
        var lifetime = TimeSpan.FromSeconds(1);
        await using var owner = new TtSlotPool<Resource>(2, 1, id => new Resource(id),
            (_, _) => Task.CompletedTask, resource => { deleted.Add(resource.Id); return Task.CompletedTask; }, timeProvider: clock);
        await owner.WarmAsync(2, default);
        clock.Advance(lifetime);
        await owner.ParkIdleAsync(floor, (_, _) => throw new InvalidOperationException("Expired surplus was parked instead of retired."), idleLifetime: lifetime);
        Assert.Equal(2 - floor, deleted.Count);
        Assert.Equal((floor, floor, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked));
    }

    [Fact]
    public async Task Expiry_uses_monotonic_idle_age_preserved_through_parking_and_releases_only_confirmed_deletions()
    {
        var clock = new ManualPoolClock();
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var deleted = new List<Guid>();
        var lifetime = TimeSpan.FromSeconds(10);
        await using var owner = new TtSlotPool<Resource>(2, 1, id =>
        {
            if (Interlocked.Increment(ref created) > 2) throw new InvalidOperationException("Idle expiry refilled without demand.");
            return new Resource(id);
        }, (_, _) => Task.CompletedTask, async resource =>
        {
            deleting.TrySetResult();
            await confirm.Task;
            deleted.Add(resource.Id);
        }, timeProvider: clock);
        await owner.WarmAsync(2, default);
        clock.Advance(lifetime - TimeSpan.FromTicks(1));
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask, idleLifetime: lifetime);
        Assert.Equal((2, 2), (owner.Snapshot.Reserved, owner.Snapshot.Parked));
        clock.JumpWallClock(TimeSpan.FromDays(100));
        await owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask, idleLifetime: lifetime);
        Assert.Empty(deleted);
        clock.Advance(TimeSpan.FromTicks(1));
        var maintenance = owner.ParkIdleAsync(0, (_, _) => Task.CompletedTask, idleLifetime: lifetime);
        try
        {
            Assert.False(maintenance.IsCompleted);
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((2, 0, 2), (owner.Snapshot.Reserved, owner.Snapshot.Parked, owner.Snapshot.Retiring));
            Assert.Empty(deleted);
            confirm.TrySetResult();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(2, deleted.Distinct().Count());
            Assert.Equal((0, 0, 2), (owner.Snapshot.Reserved, owner.Snapshot.Ready, created));
        }
        finally
        {
            confirm.TrySetResult();
            await maintenance;
        }
    }
}

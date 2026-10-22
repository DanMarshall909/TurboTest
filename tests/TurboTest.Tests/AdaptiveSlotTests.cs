using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class AdaptiveSlotTests
{
    private sealed record Resource(Guid Id, int Number);

    [Fact]
    public async Task Unchanged_capacity_monitoring_leaves_expiry_to_idle_maintenance_and_confirmed_deletion()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 3, TimeSpan.FromSeconds(5), clock, minimum: 2,
            shrinkWindow: TimeSpan.FromSeconds(5), cooldown: TimeSpan.Zero);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new List<Guid>();
        var deleted = new List<Guid>();
        var holdRetirement = false;
        Guid parked = default;
        var retirementAttempts = 0;
        var owner = new TtSlotPool<Resource>(3, 1, id =>
        {
            if (created.Count >= 3) throw new InvalidOperationException("Idle maintenance created extra capacity.");
            created.Add(id);
            return new Resource(id, created.Count);
        }, (_, _) => Task.CompletedTask, async resource =>
        {
            if (resource.Id == parked && holdRetirement)
            {
                if (++retirementAttempts > 1) throw new InvalidOperationException("Confirmed retirement was repeated.");
                entered.TrySetResult();
                await release.Task;
            }
            Assert.DoesNotContain(resource.Id, deleted);
            deleted.Add(resource.Id);
        }, rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        Task? maintenance = null;
        Task Park(Resource resource, CancellationToken _)
        {
            Assert.Equal(Guid.Empty, parked);
            parked = resource.Id;
            return Task.CompletedTask;
        }
        try
        {
            await owner.WarmAsync(3, default);
            clock.Advance(TimeSpan.FromSeconds(5));
            var shrink = await owner.TickCapacityAsync(null, Park, idleLifetime: TimeSpan.FromSeconds(10));
            Assert.Equal((3, 2, "SpareCapacity"), (shrink.PreviousTarget, shrink.Target, shrink.Reason));
            Assert.NotEqual(Guid.Empty, parked);
            Assert.Equal((3, 2, 1), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked));

            clock.Advance(TimeSpan.FromSeconds(5));
            var held = await owner.TickCapacityAsync(null, Park, idleLifetime: TimeSpan.FromSeconds(10));
            Assert.Equal((2, 2, "AtMinimum"), (held.PreviousTarget, held.Target, held.Reason));
            Assert.Empty(deleted);
            Assert.Equal((3, 2, 1), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked));

            holdRetirement = true;
            maintenance = owner.ParkIdleAsync(0, Park, idleLifetime: TimeSpan.FromSeconds(10));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(maintenance.IsCompleted);
            Assert.Empty(deleted);
            Assert.Equal((3, 2, 1), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Retiring));
            release.TrySetResult();
            await maintenance.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { parked }, deleted);
            Assert.Equal((2, 2, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.Parked));
        }
        finally
        {
            release.TrySetResult();
            if (maintenance is not null)
                await Record.ExceptionAsync(() => maintenance.WaitAsync(TimeSpan.FromSeconds(2)));
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(created.Order(), deleted.Order());
        Assert.Equal((1, 0), (retirementAttempts, owner.Snapshot.Reserved));
    }

    [Fact]
    public async Task An_unchanged_warm_target_defers_discarded_capacity_replacement_until_demand_returns()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(1, 1, TimeSpan.FromSeconds(5), clock);
        var created = new List<Guid>();
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            if (created.Count >= 2) throw new InvalidOperationException("Discard recovery exceeded its two-resource oracle.");
            created.Add(id);
            return new Resource(id, created.Count);
        }, (_, _) => Task.CompletedTask, resource =>
        {
            Assert.DoesNotContain(resource.Id, deleted);
            deleted.Add(resource.Id);
            return Task.CompletedTask;
        }, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(1, default);
        var discarded = await owner.AcquireAsync();
        await owner.DiscardAsync(discarded);
        Assert.Equal((1, 0), (owner.Snapshot.WarmTarget, owner.Snapshot.Reserved));

        var held = await owner.TickCapacityAsync(null, (_, _) => Task.CompletedTask);
        Assert.Equal((1, 1, "AtMinimum"), (held.PreviousTarget, held.Target, held.Reason));
        Assert.Single(created);
        Assert.Equal(0, owner.Snapshot.Reserved);

        var replacement = await owner.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.NotEqual(discarded.Value.Id, replacement.Value.Id);
        Assert.Equal((2, 1, 1), (created.Count, owner.Snapshot.Reserved, owner.Snapshot.Leased));
        await owner.ReturnAsync(replacement, (_, _) => Task.CompletedTask);
        await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(created.Order(), deleted.Order());
        Assert.Equal(0, owner.Snapshot.Reserved);
    }

    [Fact]
    public async Task Deferred_shrink_maintenance_counts_leased_commitments_toward_the_remaining_warm_target()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 3, TimeSpan.FromSeconds(5), clock,
            shrinkWindow: TimeSpan.FromSeconds(5), cooldown: TimeSpan.Zero);
        var parked = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, 0),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(3, default);
        var leased = await owner.AcquireAsync();
        clock.Advance(TimeSpan.FromSeconds(5));

        // A policy-only tick can defer physical parking to explicit maintenance.
        var shrink = await owner.TickCapacityAsync(null);
        Assert.Equal((3, 2, "SpareCapacity"), (shrink.PreviousTarget, shrink.Target, shrink.Reason));
        Assert.Equal((2, 1, 0), (owner.Snapshot.Ready, owner.Snapshot.Leased, owner.Snapshot.Parked));
        await owner.ParkIdleAsync(0, (resource, _) =>
        {
            parked.Add(resource.Id);
            return Task.CompletedTask;
        });

        Assert.Single(parked);
        Assert.DoesNotContain(leased.Value.Id, parked);
        Assert.Equal((2, 1, 1, 1), (owner.Snapshot.WarmTarget, owner.Snapshot.Ready,
            owner.Snapshot.Leased, owner.Snapshot.Parked));
    }

    [Fact]
    public async Task Failed_background_growth_retains_its_terminal_error_when_the_queued_caller_cancels()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(1, 2, TimeSpan.FromSeconds(5), clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var deleted = 0;
        var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, ++created), async (resource, _) =>
        {
            if (resource.Number == 1) return;
            if (resource.Number > 4) throw new InvalidOperationException("Growth exceeded its three preparation attempts.");
            entered.TrySetResult();
            await release.Task;
            throw new IOException("private-growth-failure");
        }, _ => { deleted++; return Task.CompletedTask; }, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(1, default);
        var leased = await owner.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancellation.Token);
        clock.Advance(TimeSpan.FromSeconds(5));
        var growing = owner.TickCapacityAsync(new TtMemoryReading(40, 100, clock.GetTimestamp()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            release.TrySetResult();
            await growing.WaitAsync(TimeSpan.FromSeconds(2));
            await owner.ReturnAsync(leased, (_, _) => Task.CompletedTask);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.DisposeAsync().AsTask());
            Assert.Contains("IOException", failure.Message);
            Assert.DoesNotContain("private-growth-failure", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.Equal((4, 4, 0), (created, deleted, owner.Snapshot.Reserved));
            await owner.RetryCleanupAsync();
            Assert.Equal(4, deleted);
        }
        finally
        {
            cancellation.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(() => growing);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync();
        }
    }

    [Fact]
    public async Task Recreation_keeps_its_warm_commitment_until_replacement_is_verified()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 2, TimeSpan.FromSeconds(5), clock, minimum: 2);
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var holdDeletion = true;
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, ++created),
            (_, _) => Task.CompletedTask, async _ =>
            {
                if (!holdDeletion) return;
                deleting.TrySetResult();
                await release.Task;
            }, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(2, default);
        var first = await owner.AcquireAsync();
        var second = await owner.AcquireAsync();
        var returning = owner.ReturnAsync(first, (_, _) => Task.FromResult(false));
        using var cancellation = new CancellationTokenSource();
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            waiting = owner.AcquireAsync(cancellation.Token);
            Assert.False(waiting.IsCompleted);
            Assert.Equal((2, 2), (created, owner.Snapshot.Reserved));
            holdDeletion = false;
            release.TrySetResult();
            await returning;
            var replacement = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(first.Id, replacement.Id);
            Assert.Equal((2, 2, 2), (owner.Snapshot.WarmTarget, owner.Snapshot.Leased, owner.Snapshot.Reserved));
            await owner.ReturnAsync(replacement, (_, _) => Task.CompletedTask);
        }
        finally
        {
            holdDeletion = false;
            release.TrySetResult();
            cancellation.Cancel();
            await returning;
            if (waiting is not null)
            {
                await Record.ExceptionAsync(async () =>
                {
                    var reservation = await waiting;
                    if (owner.Snapshot.Leased > 1) await owner.ReturnAsync(reservation, (_, _) => Task.CompletedTask);
                });
            }
            await owner.ReturnAsync(second, (_, _) => Task.CompletedTask);
        }
    }

    [Fact]
    public async Task Explicit_idle_maintenance_preserves_admitted_adaptive_warm_capacity_including_its_minimum()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 3, TimeSpan.FromSeconds(5), clock, minimum: 2);
        var parked = 0;
        var deleted = 0;
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, 0),
            (_, _) => Task.CompletedTask, _ => { deleted++; return Task.CompletedTask; },
            rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(2, default);
        var lease = await owner.AcquireAsync();
        try
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await owner.ParkIdleAsync(0, (_, _) => { parked++; return Task.CompletedTask; },
                maxParked: 0, idleLifetime: TimeSpan.FromSeconds(1));
            Assert.Equal((2, 1, 1, 0, 0), (owner.Snapshot.WarmTarget, owner.Snapshot.Ready,
                owner.Snapshot.Leased, parked, deleted));
        }
        finally { await owner.ReturnAsync(lease, (_, _) => Task.CompletedTask); }
        await owner.ParkIdleAsync(0, (_, _) => { parked++; return Task.CompletedTask; });
        Assert.Equal((2, 2, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, parked));
    }

    [Fact]
    public async Task Protected_ready_capacity_is_not_spare_and_does_not_age_a_shrink_window()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(4, 4, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7), readyFloor: 2);
        var parked = 0;
        await using var owner = new TtSlotPool<Resource>(4, 1, id => new Resource(id, 0),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(4, default);
        var leases = (await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => owner.AcquireAsync()))).ToList();
        Task Park(Resource _, CancellationToken __) { parked++; return Task.CompletedTask; }
        try
        {
            clock.Advance(TimeSpan.FromSeconds(20));
            Assert.Equal(4, (await owner.TickCapacityAsync(null, Park, warmFloor: 2)).Target);
            var first = leases[0]; leases.RemoveAt(0);
            await owner.ReturnAsync(first, (_, _) => Task.CompletedTask);
            Assert.Equal(4, (await owner.TickCapacityAsync(null, Park, warmFloor: 2)).Target);
            clock.Advance(TimeSpan.FromSeconds(7));
            Assert.Equal(3, (await owner.TickCapacityAsync(null, Park, warmFloor: 2)).Target);
            Assert.Equal((2, 1, 1), (owner.Snapshot.Ready, owner.Snapshot.Leased, parked));
            var last = leases[0]; leases.RemoveAt(0);
            await owner.ReturnAsync(last, (_, _) => Task.CompletedTask);
            clock.Advance(TimeSpan.FromSeconds(7));
            Assert.Equal(2, (await owner.TickCapacityAsync(null, Park, warmFloor: 2)).Target);
            Assert.Equal((2, 0, 2), (owner.Snapshot.Ready, owner.Snapshot.Leased, parked));
        }
        finally { await Task.WhenAll(leases.Select(lease => owner.ReturnAsync(lease, (_, _) => Task.CompletedTask))); }
    }

    [Fact]
    public async Task Renewed_pressure_rewarms_the_retained_identity_at_a_full_physical_cap()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 3, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7));
        var created = 0;
        var rewarms = 0;
        Guid? parkedId = null;
        var verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = new TtSlotPool<Resource>(3, 1, id =>
        {
            if (++created > 3) throw new InvalidOperationException("Retained growth attempted a fresh allocation.");
            return new Resource(id, created);
        }, (_, _) => Task.CompletedTask, _ => Task.CompletedTask, rewarm: async (resource, _) =>
        {
            Assert.Equal(parkedId, resource.Id);
            Assert.Equal(1, Interlocked.Increment(ref rewarms));
            await verified.Task;
        }, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(3, default);
        clock.Advance(TimeSpan.FromSeconds(7));
        await owner.TickCapacityAsync(null, (resource, _) => { parkedId = resource.Id; return Task.CompletedTask; });
        Assert.NotNull(parkedId);
        var leases = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => owner.AcquireAsync()));
        using var cancel = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancel.Token);
        Task<TtCapacityDecision>? growing = null;
        TtSlotPool<Resource>.Reservation? renewed = null;
        try
        {
            Assert.False(waiting.IsCompleted);
            Assert.Equal(0, rewarms);
            clock.Advance(TimeSpan.FromSeconds(5));
            growing = owner.TickCapacityAsync(new TtMemoryReading(40, 100, clock.GetTimestamp()));
            Assert.Equal(1, rewarms);
            Assert.False(growing.IsCompleted);
            Assert.False(waiting.IsCompleted);
            Assert.Equal((3, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Rewarming, owner.Snapshot.Parked));
            verified.TrySetResult();
            Assert.Equal("QueuePressure", (await growing).Reason);
            renewed = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(parkedId, renewed.Id);
            Assert.Equal((3, 3, 3), (created, owner.Snapshot.WarmTarget, owner.Snapshot.Leased));
        }
        finally
        {
            cancel.Cancel();
            verified.TrySetResult();
            if (growing is not null) await Record.ExceptionAsync(() => growing);
            await Task.WhenAll(leases.Select(lease => owner.ReturnAsync(lease, (_, _) => Task.CompletedTask)));
            if (renewed is not null) await owner.ReturnAsync(renewed, (_, _) => Task.CompletedTask);
            await Record.ExceptionAsync(() => waiting);
        }
    }

    [Fact]
    public async Task A_ready_exhaustion_between_ticks_restarts_the_actual_spare_window()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 3, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7));
        var parked = 0;
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, 0),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(3, default);
        clock.Advance(TimeSpan.FromSeconds(6));
        var leases = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => owner.AcquireAsync()));
        clock.Advance(TimeSpan.FromSeconds(20));
        await Task.WhenAll(leases.Select(lease => owner.ReturnAsync(lease, (_, _) => Task.CompletedTask)));
        Task Park(Resource _, CancellationToken __) { parked++; return Task.CompletedTask; }
        Assert.Equal(3, (await owner.TickCapacityAsync(null, Park)).Target);
        Assert.Equal(0, parked);
        clock.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        Assert.Equal(3, (await owner.TickCapacityAsync(null, Park)).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, (await owner.TickCapacityAsync(null, Park)).Target);
        Assert.Equal(1, parked);
    }

    [Fact]
    public async Task Stabilized_shrink_claims_only_one_ready_slot_and_awaits_its_actual_closure()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 3, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7));
        var created = 0;
        var parked = new List<Guid>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = new TtSlotPool<Resource>(3, 1, id => new Resource(id, ++created),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask,
            rewarm: (_, _) => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(3, default);
        var leased = await owner.AcquireAsync();
        async Task Park(Resource resource, CancellationToken _)
        {
            if (resource.Id == leased.Id || parked.Count != 0) throw new InvalidOperationException("Shrink selected active or excess capacity.");
            parked.Add(resource.Id);
            entered.TrySetResult();
            await release.Task;
        }
        clock.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        Assert.Equal(3, (await owner.TickCapacityAsync(null, Park)).Target);
        Assert.Empty(parked);
        clock.Advance(TimeSpan.FromTicks(1));
        var shrinking = owner.TickCapacityAsync(null, Park);
        try
        {
            Assert.False(shrinking.IsCompleted);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((3, 2, 1, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.WarmTarget,
                owner.Snapshot.Leased, owner.Snapshot.Ready, owner.Snapshot.Parking));
            release.TrySetResult();
            Assert.Equal("SpareCapacity", (await shrinking).Reason);
            Assert.Equal((3, 1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Parked, owner.Snapshot.Ready, owner.Snapshot.Parking));
            clock.Advance(TimeSpan.FromSeconds(50));
            await owner.TickCapacityAsync(null, Park);
            Assert.Single(parked);
            Assert.Equal(3, created);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => shrinking);
            await owner.ReturnAsync(leased, (_, _) => Task.CompletedTask);
        }
    }

    [Fact]
    public async Task Queued_demand_cannot_reserve_above_the_warm_target_until_a_stabilized_atomic_growth_claim()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 4, TimeSpan.FromSeconds(5), clock);
        var created = 0;
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var owner = new TtSlotPool<Resource>(4, 1, id =>
        {
            var number = Interlocked.Increment(ref created);
            if (number > 4) throw new InvalidOperationException("Physical cap exceeded.");
            return new Resource(id, number);
        }, async (resource, _) =>
        {
            if (resource.Number != 3) return;
            preparing.TrySetResult();
            await verified.Task;
        }, _ => Task.CompletedTask, timeProvider: clock, capacityPolicy: policy);
        await owner.WarmAsync(2, default);
        var first = await owner.AcquireAsync();
        var second = await owner.AcquireAsync();
        using var cancel = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancel.Token);
        TtSlotPool<Resource>.Reservation? third = null;
        Task<TtCapacityDecision>[] changes = [];
        try
        {
            Assert.False(waiting.IsCompleted);
            Assert.Equal((2, 2, 2, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.WarmTarget, owner.Snapshot.QueuedAcquisitions));
            clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
            var held = await owner.TickCapacityAsync(new TtMemoryReading(40, 100, clock.GetTimestamp()));
            Assert.Equal((2, "GrowthStabilizing"), (held.Target, held.Reason));
            Assert.False(waiting.IsCompleted);
            clock.Advance(TimeSpan.FromTicks(1));
            changes = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
                owner.TickCapacityAsync(new TtMemoryReading(40, 100, clock.GetTimestamp())))).ToArray();
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((3, 3, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.Preparing));
            verified.TrySetResult();
            var results = await Task.WhenAll(changes).WaitAsync(TimeSpan.FromSeconds(2));
            third = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(results, change => change.Target != change.PreviousTarget);
            Assert.Equal((3, 3, 3, 3, 0), (created, owner.Snapshot.Reserved, owner.Snapshot.WarmTarget, owner.Snapshot.Leased, owner.Snapshot.QueuedAcquisitions));
            Assert.Equal(3, new[] { first.Id, second.Id, third.Id }.Distinct().Count());
        }
        finally
        {
            cancel.Cancel();
            verified.TrySetResult();
            await Record.ExceptionAsync(() => Task.WhenAll(changes));
            await owner.ReturnAsync(first, (_, _) => Task.CompletedTask);
            await owner.ReturnAsync(second, (_, _) => Task.CompletedTask);
            if (third is not null) await owner.ReturnAsync(third, (_, _) => Task.CompletedTask);
            await Record.ExceptionAsync(() => waiting);
        }
    }
}

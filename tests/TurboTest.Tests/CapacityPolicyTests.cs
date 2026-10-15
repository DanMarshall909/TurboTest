using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CapacityPolicyTests
{
    [Fact]
    public void Shared_maintenance_permits_veto_growth_when_returns_wait_for_the_same_cleanup_budget()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 8, TimeSpan.FromSeconds(5), clock);
        var state = Busy() with { WaitingResets = 1, ExecutingCleanup = 1, Parking = 1 };
        policy.ObserveDemand(state);
        clock.Advance(TimeSpan.FromSeconds(5));
        var held = Decide(policy, clock, state);
        Assert.Equal((3, "ResetSaturated"), (held.Target, held.Reason));
        var freed = Decide(policy, clock, state with { ExecutingCleanup = 0 });
        Assert.Equal((4, "QueuePressure"), (freed.Target, freed.Reason));
    }

    [Theory]
    [InlineData("preparing", "PreparationPending")]
    [InlineData("rewarming", "PreparationPending")]
    [InlineData("quarantined", "PhysicalCapacity")]
    [InlineData("retiring", "PhysicalCapacity")]
    [InlineData("parking", "PhysicalCapacity")]
    [InlineData("unrealized", "WarmupPending")]
    public void Pending_promises_and_unavailable_physical_reservations_do_not_trigger_more_growth(string stateName, string reason)
    {
        var clock = new ManualPoolClock();
        var state = stateName switch
        {
            "preparing" => Busy(leased: 1) with { Preparing = 1 },
            "rewarming" => Busy(leased: 1) with { Rewarming = 1 },
            "quarantined" => Busy() with { Quarantined = 6 },
            "retiring" => Busy() with { Retiring = 6 },
            "parking" => Busy() with { Parking = 6 },
            _ => Busy() with { Quarantined = 1, Parked = 5 }
        };
        var startup = stateName == "unrealized" ? 3 : 2;
        var policy = new TtCapacityPolicy(startup, 8, TimeSpan.FromSeconds(5), clock);
        policy.ObserveDemand(state);
        clock.Advance(TimeSpan.FromSeconds(5));
        var decision = Decide(policy, clock, state);
        Assert.Equal((startup, reason), (decision.Target, decision.Reason));
    }

    [Theory]
    [InlineData(1, 1, "ResetSaturated", 3)]
    [InlineData(2, 1, "QueuePressure", 4)]
    [InlineData(2, 2, "ResetSaturated", 3)]
    public void Only_a_backlog_with_all_reset_permits_executing_vetoes_growth(int resetLimit, int executing, string reason, int target)
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 8, TimeSpan.FromSeconds(5), clock, resetConcurrency: resetLimit);
        var state = Busy(leased: 2 - executing) with { WaitingResets = 1, ExecutingResets = executing };
        policy.ObserveDemand(state);
        clock.Advance(TimeSpan.FromSeconds(5));
        var decision = Decide(policy, clock, state);
        Assert.Equal((target, reason), (decision.Target, decision.Reason));
    }

    [Fact]
    public void Compatible_parked_capacity_can_be_rewarmed_even_at_the_full_physical_cap()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(5), clock);
        var state = Busy() with { Parked = 6 };
        policy.ObserveDemand(state);
        clock.Advance(TimeSpan.FromSeconds(5));
        var decision = Decide(policy, clock, state);
        Assert.Equal((3, "QueuePressure"), (decision.Target, decision.Reason));
    }

    [Fact]
    public void Missing_or_invalid_memory_readings_inhibit_growth_with_named_reasons()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(5), clock);
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(5));
        var missing = policy.Evaluate(Busy());
        Assert.Equal((2, "ResourceMissing"), (missing.Target, missing.Reason));
        foreach (var reading in new[]
        {
            new TtMemoryReading(40, 0, clock.GetTimestamp()),
            new TtMemoryReading(-1, 100, clock.GetTimestamp()),
            new TtMemoryReading(101, 100, clock.GetTimestamp()),
            new TtMemoryReading(40, 100, clock.GetTimestamp() + 1)
        })
        {
            var invalid = policy.Evaluate(Busy(), reading);
            Assert.Equal((2, "ResourceInvalid"), (invalid.Target, invalid.Reason));
        }
        Assert.Equal(3, Decide(policy, clock, Busy()).Target);
    }

    [Fact]
    public void Memory_freshness_uses_monotonic_sample_age_and_accepts_the_exact_maximum_age()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(1), clock);
        var reading = new TtMemoryReading(40, 100, clock.GetTimestamp());
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(3) + TimeSpan.FromTicks(1));
        var stale = policy.Evaluate(Busy(), reading);
        Assert.Equal((2, "ResourceStale"), (stale.Target, stale.Reason));
        var boundary = reading with { ObservedAt = 1 };
        clock.JumpWallClock(TimeSpan.FromDays(100));
        Assert.Equal(3, policy.Evaluate(Busy(), boundary).Target);
    }

    [Fact]
    public void Growth_rejects_the_memory_ceiling_inclusively_but_missing_readings_do_not_block_eligible_shrink()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(5), clock);
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(5));
        var pressure = policy.Evaluate(Busy(), new TtMemoryReading(80, 100, clock.GetTimestamp()));
        Assert.Equal((2, "MemoryPressure"), (pressure.Target, pressure.Reason));
        Assert.Equal(3, policy.Evaluate(Busy(), new TtMemoryReading(79, 100, clock.GetTimestamp())).Target);
        policy.ObserveDemand(Idle(3));
        clock.Advance(TimeSpan.FromSeconds(10));
        var shrinking = policy.Evaluate(Idle(3));
        Assert.Equal((2, "SpareCapacity"), (shrinking.Target, shrinking.Reason));
    }

    private static TtPoolSnapshot Busy(int queued = 6, int leased = 2) => new(Guid.Empty, 8, 0, 0, leased, 0, 0, 0, 0, 0, queued);
    private static TtPoolSnapshot Idle(int ready, int leased = 0) => new(Guid.Empty, 8, 0, ready, leased, 0, 0, 0, 0, 0, 0);
    private static TtCapacityDecision Decide(TtCapacityPolicy policy, ManualPoolClock clock, TtPoolSnapshot state) =>
        policy.Evaluate(state, new TtMemoryReading(40, 100, clock.GetTimestamp()));

    [Fact]
    public void Continuous_spare_capacity_shrinks_one_ready_slot_with_cooldown_and_a_total_warm_floor()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(4, 8, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7), cooldown: TimeSpan.FromSeconds(11));
        policy.ObserveDemand(Idle(4));
        clock.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        Assert.Equal(4, Decide(policy, clock, Idle(4)).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        var shrink = Decide(policy, clock, Idle(4));
        Assert.Equal((4, 3, "SpareCapacity"), (shrink.PreviousTarget, shrink.Target, shrink.Reason));
        policy.ObserveDemand(Idle(1, 2));
        clock.Advance(TimeSpan.FromSeconds(11) - TimeSpan.FromTicks(1));
        var held = Decide(policy, clock, Idle(1, 2));
        Assert.Equal((3, "Cooldown"), (held.Target, held.Reason));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, Decide(policy, clock, Idle(1, 2)).Target);
        clock.Advance(TimeSpan.FromSeconds(50));
        var floor = Decide(policy, clock, Idle(0, 2));
        Assert.Equal((2, "AtMinimum"), (floor.Target, floor.Reason));
    }

    [Fact]
    public void A_busy_gap_restarts_spare_stabilization_and_no_ready_slot_can_be_selected()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(3, 8, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7));
        policy.ObserveDemand(Idle(3));
        clock.Advance(TimeSpan.FromSeconds(6));
        policy.ObserveDemand(Idle(0, 3));
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(3, Decide(policy, clock, Idle(0, 3)).Target);
        policy.ObserveDemand(Idle(1, 2));
        clock.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1));
        Assert.Equal(3, Decide(policy, clock, Idle(1, 2)).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(2, Decide(policy, clock, Idle(1, 2)).Target);
    }

    [Fact]
    public void Growth_and_shrink_share_a_cooldown_across_direction_changes()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(5), clock,
            minimum: 2, shrinkWindow: TimeSpan.FromSeconds(7), cooldown: TimeSpan.FromSeconds(11));
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, Decide(policy, clock, Busy()).Target);
        policy.ObserveDemand(Idle(1, 2));
        clock.Advance(TimeSpan.FromSeconds(7));
        var shrinking = Decide(policy, clock, Idle(1, 2));
        Assert.Equal((3, "Cooldown"), (shrinking.Target, shrinking.Reason));
        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(2, Decide(policy, clock, Idle(1, 2)).Target);
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(5));
        var growing = Decide(policy, clock, Busy());
        Assert.Equal((2, "Cooldown"), (growing.Target, growing.Reason));
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(3, Decide(policy, clock, Busy()).Target);
    }

    [Fact]
    public void Continuous_pressure_grows_one_step_at_the_boundary_and_stops_at_the_warm_cap()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 4, TimeSpan.FromSeconds(5), clock);
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        Assert.Equal(2, Decide(policy, clock, Busy()).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        var growth = Decide(policy, clock, Busy());
        Assert.Equal((2, 3, "QueuePressure"), (growth.PreviousTarget, growth.Target, growth.Reason));
        Assert.Equal(3, Decide(policy, clock, Busy(leased: 3)).Target);
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(4, Decide(policy, clock, Busy(leased: 3)).Target);
        clock.Advance(TimeSpan.FromSeconds(50));
        var capped = Decide(policy, clock, Busy(leased: 4));
        Assert.Equal((4, 4, "AtMaximum"), (capped.PreviousTarget, capped.Target, capped.Reason));
    }

    [Fact]
    public void Pressure_interrupted_between_ticks_restarts_the_window_and_ignores_wall_clock_jumps()
    {
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(2, 8, TimeSpan.FromSeconds(5), clock);
        policy.ObserveDemand(Busy());
        clock.Advance(TimeSpan.FromSeconds(4));
        policy.ObserveDemand(Busy(0));
        var idle = Decide(policy, clock, Busy(0));
        Assert.Equal((2, "NoDemand"), (idle.Target, idle.Reason));
        clock.Advance(TimeSpan.FromSeconds(1));
        policy.ObserveDemand(Busy());
        clock.JumpWallClock(TimeSpan.FromDays(100));
        Assert.Equal(2, Decide(policy, clock, Busy()).Target);
        clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
        Assert.Equal(2, Decide(policy, clock, Busy()).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(3, Decide(policy, clock, Busy()).Target);
    }
}

using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class AdaptiveConfigurationTests
{
    [Fact]
    public void Captured_resource_thresholds_and_age_control_decisions_after_options_change()
    {
        var options = new TtOptions
        {
            EnableDynamicScaling = true, PoolSize = 6, StartupWarmDatabases = 3, WarmFloor = 1,
            AdaptivePool = new() { MinimumWarmDatabases = 2, MaximumWarmDatabases = 5,
                GrowthStabilization = TimeSpan.FromSeconds(3), ShrinkStabilization = TimeSpan.FromSeconds(7),
                Cooldown = TimeSpan.FromSeconds(11), ResourceMaximumAge = TimeSpan.FromSeconds(13), MemoryGrowthCeilingPercent = 61 }
        };
        var captured = TtAdaptiveConfiguration.Capture(options)!;
        options.StartupWarmDatabases = 1;
        options.AdaptivePool = new() { ResourceMaximumAge = TimeSpan.FromSeconds(1), MemoryGrowthCeilingPercent = 100 };
        var clock = new ManualPoolClock();
        var policy = new TtCapacityPolicy(captured, clock);
        var state = new TtPoolSnapshot(Guid.Empty, 6, 0, 0, 3, 0, 0, 0, 0, 0, 5);
        policy.ObserveDemand(state);
        clock.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromTicks(1));
        Assert.Equal(3, policy.Evaluate(state, new TtMemoryReading(60, 100, 0)).Target);
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(4, policy.Evaluate(state, new TtMemoryReading(60, 100, 0)).Target);
        state = state with { Leased = 4 };
        var limited = policy.Evaluate(state, new TtMemoryReading(61, 100, clock.GetTimestamp()));
        Assert.Equal((4, "MemoryPressure"), (limited.Target, limited.Reason));
        clock.Advance(TimeSpan.FromSeconds(11) + TimeSpan.FromTicks(1));
        var stale = policy.Evaluate(state, new TtMemoryReading(60, 100, TimeSpan.FromSeconds(1).Ticks));
        Assert.Equal((4, "ResourceStale"), (stale.Target, stale.Reason));
        var boundary = policy.Evaluate(state, new TtMemoryReading(60, 100, TimeSpan.FromSeconds(1).Ticks + 1));
        Assert.Equal((5, "QueuePressure"), (boundary.Target, boundary.Reason));
    }

    [Fact]
    public void Defaults_resolve_the_physical_maximum_and_adaptive_minimum_startup()
    {
        var captured = TtAdaptiveConfiguration.Capture(new TtOptions { PoolSize = 6, EnableDynamicScaling = true });
        Assert.NotNull(captured);
        Assert.Equal((1, 6, 1), (captured.Minimum, captured.Maximum, captured.Startup));
    }

    [Theory]
    [InlineData("minimum-zero", "MinimumWarmDatabases")]
    [InlineData("minimum-over-cap", "MinimumWarmDatabases")]
    [InlineData("maximum-below-minimum", "MaximumWarmDatabases")]
    [InlineData("maximum-over-cap", "MaximumWarmDatabases")]
    [InlineData("startup-below-minimum", "StartupWarmDatabases")]
    [InlineData("startup-over-maximum", "StartupWarmDatabases")]
    [InlineData("ready-floor-over-minimum", "WarmFloor")]
    [InlineData("growth-zero", "GrowthStabilization")]
    [InlineData("shrink-zero", "ShrinkStabilization")]
    [InlineData("cooldown-negative", "Cooldown")]
    [InlineData("interval-zero", "CheckInterval")]
    [InlineData("interval-submillisecond", "CheckInterval")]
    [InlineData("interval-over-timer", "CheckInterval")]
    [InlineData("resource-age-zero", "ResourceMaximumAge")]
    [InlineData("resource-age-shorter-than-interval", "ResourceMaximumAge")]
    [InlineData("memory-zero", "MemoryGrowthCeilingPercent")]
    [InlineData("memory-over-percent", "MemoryGrowthCeilingPercent")]
    public void Invalid_policy_limits_and_windows_are_rejected_when_captured(string fault, string parameter)
    {
        var options = new TtOptions { EnableDynamicScaling = true, PoolSize = 6 };
        options.AdaptivePool = fault switch
        {
            "minimum-zero" => new() { MinimumWarmDatabases = 0 },
            "minimum-over-cap" => new() { MinimumWarmDatabases = 7 },
            "maximum-below-minimum" => new() { MinimumWarmDatabases = 3, MaximumWarmDatabases = 2 },
            "maximum-over-cap" => new() { MaximumWarmDatabases = 7 },
            "growth-zero" => new() { GrowthStabilization = TimeSpan.Zero },
            "shrink-zero" => new() { ShrinkStabilization = TimeSpan.Zero },
            "cooldown-negative" => new() { Cooldown = TimeSpan.FromTicks(-1) },
            "interval-zero" => new() { CheckInterval = TimeSpan.Zero },
            "interval-submillisecond" => new() { CheckInterval = TimeSpan.FromTicks(9999) },
            "interval-over-timer" => new() { CheckInterval = TimeSpan.FromMilliseconds(uint.MaxValue) },
            "resource-age-zero" => new() { ResourceMaximumAge = TimeSpan.Zero },
            "resource-age-shorter-than-interval" => new() { CheckInterval = TimeSpan.FromSeconds(4), ResourceMaximumAge = TimeSpan.FromSeconds(3) },
            "memory-zero" => new() { MemoryGrowthCeilingPercent = 0 },
            "memory-over-percent" => new() { MemoryGrowthCeilingPercent = 101 },
            _ => new()
        };
        if (fault == "startup-below-minimum") options.StartupWarmDatabases = 0;
        if (fault == "startup-over-maximum") options.StartupWarmDatabases = 7;
        if (fault == "ready-floor-over-minimum") options.WarmFloor = 2;
        var failure = Assert.ThrowsAny<ArgumentException>(() => TtAdaptiveConfiguration.Capture(options));
        Assert.Equal(parameter, failure.ParamName);
    }

    [Fact]
    public void Fixed_mode_has_no_adaptive_decisions_even_with_unused_invalid_policy_settings()
    {
        var options = new TtOptions { EnableDynamicScaling = false, AdaptivePool = new() { MinimumWarmDatabases = -1 } };
        Assert.Null(TtAdaptiveConfiguration.Capture(options));
    }
}

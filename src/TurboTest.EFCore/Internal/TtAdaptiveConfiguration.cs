namespace TurboTest.EFCore.Internal;

internal sealed record TtAdaptiveConfiguration(int Minimum, int Maximum, int Startup, int ReadyFloor,
    int ResetConcurrency, TimeSpan Growth, TimeSpan Shrink, TimeSpan Cooldown, TimeSpan CheckInterval,
    TimeSpan ResourceMaximumAge, int MemoryCeilingPercent)
{
    internal static TtAdaptiveConfiguration? Capture(TtOptions options)
    {
        if (!options.EnableDynamicScaling) return null;
        var policy = options.AdaptivePool ?? throw new ArgumentNullException(nameof(TtOptions.AdaptivePool));
        var minimum = policy.MinimumWarmDatabases;
        var maximum = policy.MaximumWarmDatabases ?? options.PoolSize;
        var startup = options.StartupWarmDatabases ?? minimum;
        if (minimum < 1 || minimum > options.PoolSize)
            throw new ArgumentOutOfRangeException(nameof(policy.MinimumWarmDatabases));
        if (maximum < minimum || maximum > options.PoolSize)
            throw new ArgumentOutOfRangeException(nameof(policy.MaximumWarmDatabases));
        if (startup < minimum || startup > maximum)
            throw new ArgumentOutOfRangeException(nameof(options.StartupWarmDatabases));
        if (options.WarmFloor < 0 || options.WarmFloor > minimum)
            throw new ArgumentOutOfRangeException(nameof(options.WarmFloor));
        Positive(policy.GrowthStabilization, nameof(policy.GrowthStabilization));
        Positive(policy.ShrinkStabilization, nameof(policy.ShrinkStabilization));
        if (policy.Cooldown < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(policy.Cooldown));
        if (policy.CheckInterval < TimeSpan.FromMilliseconds(1) ||
            policy.CheckInterval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(policy.CheckInterval));
        if (policy.ResourceMaximumAge <= TimeSpan.Zero || policy.ResourceMaximumAge < policy.CheckInterval)
            throw new ArgumentOutOfRangeException(nameof(policy.ResourceMaximumAge));
        if (policy.MemoryGrowthCeilingPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(policy.MemoryGrowthCeilingPercent));
        return new(minimum, maximum, startup, options.WarmFloor, options.ResetConcurrency,
            policy.GrowthStabilization, policy.ShrinkStabilization, policy.Cooldown, policy.CheckInterval,
            policy.ResourceMaximumAge, policy.MemoryGrowthCeilingPercent);
    }

    private static void Positive(TimeSpan value, string parameter)
    {
        if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(parameter);
    }
}

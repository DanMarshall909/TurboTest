namespace TurboTest.EFCore;

/// <summary>Captured prepared-pool warm policy. Fixed mode remains the default.</summary>
public sealed record TtAdaptiveOptions
{
    public int MinimumWarmDatabases { get; init; } = 1;
    public int? MaximumWarmDatabases { get; init; }
    public TimeSpan GrowthStabilization { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ShrinkStabilization { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan ResourceMaximumAge { get; init; } = TimeSpan.FromSeconds(3);
    public int MemoryGrowthCeilingPercent { get; init; } = 80;
}

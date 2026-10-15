namespace TurboTest.EFCore.Internal;

internal sealed record TtCapacityDecision(int PreviousTarget, int Target, string Reason);
internal sealed record TtMemoryReading(long WorkingSetBytes, long LimitBytes, long ObservedAt);

/// <summary>Monotonic demand policy; the slot ledger owns and applies physical claims.</summary>
internal sealed class TtCapacityPolicy
{
    private readonly int _maximum;
    private readonly int _minimum;
    private readonly int _resetConcurrency;
    private readonly int _readyFloor;
    private readonly TimeSpan _growthWindow;
    private readonly TimeSpan _shrinkWindow;
    private readonly TimeSpan _cooldown;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _resourceMaximumAge = TimeSpan.FromSeconds(3);
    private readonly int _memoryCeilingPercent = 80;
    private long? _pressureSince;
    private long? _spareSince;
    private long? _lastChange;

    internal TtCapacityPolicy(TtAdaptiveConfiguration settings, TimeProvider clock)
        : this(settings.Startup, settings.Maximum, settings.Growth, clock, settings.Minimum,
            settings.Shrink, settings.Cooldown, settings.ResetConcurrency, settings.ReadyFloor)
    {
        _resourceMaximumAge = settings.ResourceMaximumAge;
        _memoryCeilingPercent = settings.MemoryCeilingPercent;
    }

    internal TtCapacityPolicy(int startup, int maximum, TimeSpan growthWindow, TimeProvider clock,
        int minimum = 1, TimeSpan? shrinkWindow = null, TimeSpan? cooldown = null, int resetConcurrency = 1,
        int readyFloor = 0)
    {
        Target = startup;
        _maximum = maximum;
        _minimum = minimum;
        _resetConcurrency = resetConcurrency;
        _readyFloor = readyFloor;
        _growthWindow = growthWindow;
        _shrinkWindow = shrinkWindow ?? TimeSpan.FromSeconds(10);
        _cooldown = cooldown ?? TimeSpan.FromSeconds(2);
        _clock = clock;
    }
    internal int Target { get; private set; }

    internal void ObserveDemand(TtPoolSnapshot state)
    {
        if (state.QueuedAcquisitions == 0) _pressureSince = null;
        else _pressureSince ??= _clock.GetTimestamp();
        if (state.QueuedAcquisitions != 0 || state.Ready <= _readyFloor || Target <= _minimum) _spareSince = null;
        else _spareSince ??= _clock.GetTimestamp();
    }

    internal TtCapacityDecision Evaluate(TtPoolSnapshot state, TtMemoryReading? memory = null)
    {
        ObserveDemand(state);
        if (_pressureSince is { } pressure)
        {
            if (Target >= _maximum) return Hold("AtMaximum");
            if (ResourceConstraint(memory) is { } constraint) return Hold(constraint);
            if (state.Preparing > 0 || state.Rewarming > 0) return Hold("PreparationPending");
            if (state.Ready + state.Leased + state.WaitingResets + state.ExecutingResets < Target) return Hold("WarmupPending");
            if (state.WaitingResets > 0 && Math.Max(state.ExecutingResets, state.ExecutingCleanup) >= _resetConcurrency)
                return Hold("ResetSaturated");
            if (state.Reserved >= state.SlotLimit && state.Parked == 0) return Hold("PhysicalCapacity");
            if (CoolingDown()) return Hold("Cooldown");
            if (_clock.GetElapsedTime(pressure) < _growthWindow) return Hold("GrowthStabilizing");
            return Change(1, "QueuePressure");
        }
        if (Target <= _minimum) return Hold("AtMinimum");
        if (_spareSince is not { } spare) return Hold("NoDemand");
        if (CoolingDown()) return Hold("Cooldown");
        if (_clock.GetElapsedTime(spare) < _shrinkWindow) return Hold("ShrinkStabilizing");
        return Change(-1, "SpareCapacity");
    }

    private bool CoolingDown() => _lastChange is { } changed && _clock.GetElapsedTime(changed) < _cooldown;
    private string? ResourceConstraint(TtMemoryReading? memory)
    {
        if (memory is null) return "ResourceMissing";
        var now = _clock.GetTimestamp();
        if (memory.LimitBytes <= 0 || memory.WorkingSetBytes < 0 || memory.WorkingSetBytes > memory.LimitBytes || memory.ObservedAt > now)
            return "ResourceInvalid";
        if (_clock.GetElapsedTime(memory.ObservedAt, now) > _resourceMaximumAge) return "ResourceStale";
        if ((double)memory.WorkingSetBytes / memory.LimitBytes >= _memoryCeilingPercent / 100d) return "MemoryPressure";
        return null;
    }
    private TtCapacityDecision Hold(string reason) => new(Target, Target, reason);
    private TtCapacityDecision Change(int direction, string reason)
    {
        var previous = Target;
        Target += direction;
        var now = _clock.GetTimestamp();
        _lastChange = now;
        if (_pressureSince is not null) _pressureSince = now;
        if (_spareSince is not null) _spareSince = now;
        return new(previous, Target, reason);
    }
}

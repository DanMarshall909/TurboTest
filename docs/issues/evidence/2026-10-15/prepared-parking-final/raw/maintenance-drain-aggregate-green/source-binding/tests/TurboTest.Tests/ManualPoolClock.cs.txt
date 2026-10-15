namespace TurboTest.Tests;

internal sealed class ManualPoolClock : TimeProvider
{
    private long _timestamp;
    private long _utcTicks = DateTimeOffset.UnixEpoch.UtcTicks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);
    internal void Advance(TimeSpan duration)
    {
        Interlocked.Add(ref _timestamp, duration.Ticks);
        Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
    internal void JumpWallClock(TimeSpan change) => Interlocked.Add(ref _utcTicks, change.Ticks);
}

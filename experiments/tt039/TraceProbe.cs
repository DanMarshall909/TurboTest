using System.Collections.Concurrent;
using System.Diagnostics;

namespace TurboTest.EFCore.Internal;

// Compiled only into this experimental executable. No observer or API ships.
internal static class TraceProbe
{
    internal static readonly AsyncLocal<string?> Request = new();
    internal static readonly ConcurrentQueue<Boundary> Events = new();
    internal static bool Enabled = true;
    internal static bool AwaitReturn;
    internal static int ActiveReturns;

    internal sealed record Boundary(long Tick, string Stage, string? Request, Guid? Slot,
        Guid? Reset, string? Database, uint? Oid);

    internal static void Emit(string stage, Guid? slot = null, Guid? reset = null,
        string? request = null, string? database = null, uint? oid = null)
    {
        if (!Enabled) return;
        var tick = Stopwatch.GetTimestamp();
        Events.Enqueue(new(tick, stage, request ?? Request.Value, slot, reset, database, oid));
        if (Request.Value is not null && stage == "reset-request") Interlocked.Increment(ref ActiveReturns);
        if (Request.Value is not null && stage == (AwaitReturn ? "complete" : "background-complete"))
            Interlocked.Decrement(ref ActiveReturns);
    }
}

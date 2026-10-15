using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TurboTest.EFCore.Internal;

/// <summary>Monotonic, payload-free observations which never change owned work.</summary>
internal sealed class TtTelemetry(ILogger? logger, LogLevel minimum, Func<TtPoolSnapshot> snapshot,
    TimeProvider? timeProvider = null)
{
    private readonly ILogger _logger = new TtLogger(logger ?? NullLogger.Instance, minimum);
    internal TimeProvider Clock { get; } = timeProvider ?? TimeProvider.System;

    internal static string Outcome(Exception error) => error switch
    {
        OperationCanceledException => "Canceled",
        ObjectDisposedException => "Closed",
        _ => "Failed"
    };

    internal void Record(string operation, TimeSpan duration, string outcome)
    {
        try
        {
            var state = snapshot();
            _logger.LogDebug(new EventId(402, "PoolTiming"),
                "Pool {OwnerId} {Operation}: duration {DurationMilliseconds} ms, outcome {Outcome}, limit {SlotLimit}, reserved {Reserved}, ready {Ready}, leased {Leased}, preparing {Preparing}, rewarming {Rewarming}, parking {Parking}, parked {Parked}, waiting resets {WaitingResets}, executing resets {ExecutingResets}, quarantined {Quarantined}, retiring {Retiring}, queued {QueuedAcquisitions}",
                state.OwnerId, operation, duration.TotalMilliseconds, outcome, state.SlotLimit, state.Reserved,
                state.Ready, state.Leased, state.Preparing, state.Rewarming, state.Parking, state.Parked,
                state.WaitingResets, state.ExecutingResets, state.Quarantined, state.Retiring, state.QueuedAcquisitions);
        }
        catch (Exception) { /* Observers cannot replace operation results. */ }
    }

    internal Task MeasureAsync(string operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });

    internal async Task<TResult> MeasureAsync<TResult>(string operation, Func<Task<TResult>> action)
    {
        var started = Clock.GetTimestamp();
        var outcome = "Succeeded";
        try { return await action(); }
        catch (Exception error) { outcome = Outcome(error); throw; }
        finally { Record(operation, Clock.GetElapsedTime(started), outcome); }
    }
}

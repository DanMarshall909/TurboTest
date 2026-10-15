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

    internal void RecordCapacity(TtCapacityDecision decision, TtMemoryReading? memory, TimeSpan duration, string outcome)
    {
        try
        {
            var state = snapshot();
            double? age = memory is null ? null : Clock.GetElapsedTime(memory.ObservedAt).TotalMilliseconds;
            _logger.LogDebug(new EventId(403, "PoolCapacity"),
                "Pool capacity {OwnerId}: reason {Reason}, previous {PreviousTarget}, target {Target}, duration {DurationMilliseconds} ms, outcome {Outcome}, warm target {WarmTarget}, limit {SlotLimit}, reserved {Reserved}, ready {Ready}, leased {Leased}, preparing {Preparing}, rewarming {Rewarming}, parking {Parking}, parked {Parked}, quarantined {Quarantined}, retiring {Retiring}, queued {QueuedAcquisitions}, waiting resets {WaitingResets}, executing resets {ExecutingResets}, executing cleanup {ExecutingCleanup}, working set {MemoryWorkingSetBytes}, memory limit {MemoryLimitBytes}, resource age {ResourceAgeMilliseconds} ms",
                state.OwnerId, decision.Reason, decision.PreviousTarget, decision.Target, duration.TotalMilliseconds, outcome,
                state.WarmTarget, state.SlotLimit, state.Reserved, state.Ready, state.Leased, state.Preparing, state.Rewarming,
                state.Parking, state.Parked, state.Quarantined, state.Retiring, state.QueuedAcquisitions,
                state.WaitingResets, state.ExecutingResets, state.ExecutingCleanup,
                memory?.WorkingSetBytes, memory?.LimitBytes, age);
        }
        catch (Exception) { /* Observers cannot replace a capacity decision or its actual work. */ }
    }

    internal async Task<TResult> MeasureAsync<TResult>(string operation, Func<Task<TResult>> action)
    {
        var started = Clock.GetTimestamp();
        var outcome = "Succeeded";
        try { return await action(); }
        catch (Exception error) { outcome = Outcome(error); throw; }
        finally { Record(operation, Clock.GetElapsedTime(started), outcome); }
    }
}

using Microsoft.Extensions.Logging;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CapacityTelemetryTests
{
    [Fact]
    public async Task Exhausted_growth_reports_failed_realization_and_future_ticks_hold_unavailable_capacity()
    {
        var clock = new ManualPoolClock();
        var logger = new CapacityLogger(false);
        var policy = new TtCapacityPolicy(1, 3, TimeSpan.FromSeconds(5), clock);
        var prepared = 0;
        await using var owner = new TtSlotPool<object>(3, 1, _ => new object(), (_, _) =>
        {
            var attempt = ++prepared;
            if (attempt > 4) throw new InvalidOperationException("Growth exceeded its finite attempts.");
            return attempt == 1 ? Task.CompletedTask : Task.FromException(new IOException("private-growth-detail"));
        }, _ => Task.CompletedTask, timeProvider: clock, logger: logger, capacityPolicy: policy);
        await owner.WarmAsync(1, default);
        var first = await owner.AcquireAsync();
        var waiting = owner.AcquireAsync();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            await owner.TickCapacityAsync(new TtMemoryReading(37, 100, clock.GetTimestamp()));
            await Assert.ThrowsAsync<TurboTest.EFCore.TtLifecycleException>(() => waiting);
            var failed = Assert.Single(logger.Records);
            Assert.Equal(("QueuePressure", "Failed", 2), (failed["Reason"], failed["Outcome"], failed["Target"]));
            clock.Advance(TimeSpan.FromMinutes(1));
            var held = await owner.TickCapacityAsync(new TtMemoryReading(37, 100, clock.GetTimestamp()));
            Assert.Equal((2, 2, "Unavailable"), (held.PreviousTarget, held.Target, held.Reason));
            Assert.Equal(4, prepared);
        }
        finally { await owner.ReturnAsync(first, (_, _) => Task.CompletedTask); }
    }

    [Fact]
    public async Task Failed_shrink_reports_actual_failure_and_closure_duration_without_replacing_the_error()
    {
        var clock = new ManualPoolClock();
        var logger = new CapacityLogger(false);
        var policy = new TtCapacityPolicy(2, 2, TimeSpan.FromSeconds(5), clock,
            shrinkWindow: TimeSpan.FromSeconds(7));
        await using var owner = new TtSlotPool<object>(2, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, timeProvider: clock, logger: logger, capacityPolicy: policy);
        await owner.WarmAsync(2, default);
        clock.Advance(TimeSpan.FromSeconds(7));
        var failure = await Assert.ThrowsAsync<TurboTest.EFCore.TtLifecycleException>(() => owner.TickCapacityAsync(null, (_, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(11));
            throw new IOException("private-closure-detail");
        }));
        Assert.Contains("IOException", failure.Message);
        Assert.DoesNotContain("private-closure-detail", failure.ToString());
        var decision = Assert.Single(logger.Records);
        Assert.Equal(("SpareCapacity", "Failed", 11d), (decision["Reason"], decision["Outcome"], decision["DurationMilliseconds"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Decisions_report_safe_reasons_state_resource_age_and_actual_maintenance_latency(bool throwingObserver)
    {
        var clock = new ManualPoolClock();
        var logger = new CapacityLogger(throwingObserver);
        var policy = new TtCapacityPolicy(1, 2, TimeSpan.FromSeconds(5), clock);
        var prepared = 0;
        await using var owner = new TtSlotPool<object>(3, 1, _ => new object(), (_, _) =>
        {
            if (++prepared > 1) clock.Advance(TimeSpan.FromMilliseconds(7));
            return Task.CompletedTask;
        }, _ => Task.CompletedTask, timeProvider: clock, logger: logger, capacityPolicy: policy);
        await owner.WarmAsync(1, default);
        var first = await owner.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancellation.Token);
        try
        {
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("ResourceMissing", (await owner.TickCapacityAsync(null)).Reason);
            var missing = Assert.Single(logger.Records);
            Assert.Equal(("ResourceMissing", 1, 1, 1, 1), (missing["Reason"], missing["PreviousTarget"],
                missing["Target"], missing["Leased"], missing["QueuedAcquisitions"]));
            Assert.Null(missing["MemoryWorkingSetBytes"]);
            var reading = new TtMemoryReading(37, 100, clock.GetTimestamp());
            Assert.Equal("QueuePressure", (await owner.TickCapacityAsync(reading)).Reason);
            var grown = logger.Records.Last();
            Assert.Equal(("QueuePressure", 1, 2, 2, 2, 0), (grown["Reason"], grown["PreviousTarget"], grown["Target"],
                grown["WarmTarget"], grown["Leased"], grown["QueuedAcquisitions"]));
            Assert.Equal((37L, 100L, 7d, 7d, "Succeeded"), (grown["MemoryWorkingSetBytes"], grown["MemoryLimitBytes"],
                grown["ResourceAgeMilliseconds"], grown["DurationMilliseconds"], grown["Outcome"]));
            Assert.Equal(owner.Snapshot.OwnerId, grown["OwnerId"]);
            var second = await waiting;
            await owner.ReturnAsync(second, (_, _) => Task.CompletedTask);
        }
        finally
        {
            cancellation.Cancel();
            await Record.ExceptionAsync(() => waiting);
            await owner.ReturnAsync(first, (_, _) => Task.CompletedTask);
        }
    }

    private sealed class CapacityLogger(bool throwing) : ILogger
    {
        internal List<Dictionary<string, object?>> Records { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 403) return;
            Assert.Equal("PoolCapacity", eventId.Name);
            Records.Add(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
            if (throwing) throw new IOException("private-observer-detail");
        }
    }
}

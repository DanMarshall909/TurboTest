using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotTimingTests
{
    [Fact]
    public async Task Synchronous_cancellation_diagnostics_run_after_releasing_the_ledger_lock()
    {
        using var cancellation = new CancellationTokenSource();
        var clock = new CancelOnTimestampClock(cancellation);
        TtSlotPool<object>? pool = null;
        var blocked = false;
        var logger = new FactoryTimingTests.TimingLogger
        {
            OnRecord = record =>
            {
                if (!Equals(record["Operation"], "QueueWait") || !Equals(record["Outcome"], "Canceled")) return;
                try { Task.Run(() => pool!.Snapshot).WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult(); }
                catch (TimeoutException) { blocked = true; }
            }
        };
        await using var owner = pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, timeProvider: clock, logger: logger);
        var leased = await pool.AcquireAsync();
        clock.Armed = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.AcquireAsync(cancellation.Token));
        Assert.False(blocked, "The diagnostic consumer could not read state because the ledger lock remained held.");
        await pool.ReturnAsync(leased, (_, _) => Task.CompletedTask);
    }

    [Fact]
    public async Task Retained_rewarm_separates_permit_wait_and_verified_execution()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var firstEntered = Signal(); var secondEntered = Signal();
        var firstRelease = Signal(); var secondRelease = Signal();
        var preparations = 0; var rewarms = 0;
        await using var pool = new TtSlotPool<object>(2, 1, _ => new object(), (_, _) =>
        {
            if (++preparations > 2) throw new InvalidOperationException("Retained reuse cloned extra capacity.");
            return Task.CompletedTask;
        }, _ => Task.CompletedTask, rewarm: async (_, _) =>
        {
            switch (Interlocked.Increment(ref rewarms))
            {
                case 1: firstEntered.SetResult(); await firstRelease.Task; break;
                case 2: secondEntered.SetResult(); await secondRelease.Task; break;
                default: throw new InvalidOperationException("Repeated rewarm.");
            }
        }, timeProvider: clock, logger: logger);
        await pool.WarmAsync(2, default);
        await pool.ParkIdleAsync(0, (_, _) => Task.CompletedTask);
        logger.Records.Clear();
        var first = pool.AcquireAsync(); await firstEntered.Task;
        var second = pool.AcquireAsync();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(2)); firstRelease.SetResult();
            var one = await first; await secondEntered.Task;
            clock.Advance(TimeSpan.FromSeconds(3)); secondRelease.SetResult();
            var two = await second;
            await Task.WhenAll(one.Settled.Task, two.Settled.Task);
            Assert.Equal(new[] { 0d, 2000d }, Durations(logger, "RewarmPermitWait"));
            Assert.Equal(new[] { 2000d, 3000d }, Durations(logger, "RewarmExecution"));
            Assert.Equal(new[] { 2000d, 5000d }, Durations(logger, "QueueWait"));
            Assert.Equal(2, preparations);
            await pool.ReturnAsync(one, (_, _) => Task.CompletedTask);
            await pool.ReturnAsync(two, (_, _) => Task.CompletedTask);
        }
        finally { firstRelease.TrySetResult(); secondRelease.TrySetResult(); await Task.WhenAll(first, second); }
    }

    [Fact]
    public async Task Schema_recreation_is_separate_from_row_reset_execution()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var preparations = 0;
        await using var pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) =>
        {
            if (++preparations == 2) clock.Advance(TimeSpan.FromSeconds(5));
            if (preparations > 2) throw new InvalidOperationException("Repeated recreation.");
            return Task.CompletedTask;
        }, _ => { clock.Advance(TimeSpan.FromSeconds(3)); return Task.CompletedTask; }, timeProvider: clock, logger: logger);
        var lease = await pool.AcquireAsync();
        logger.Records.Clear();
        await pool.ReturnAsync(lease, (_, _) => { clock.Advance(TimeSpan.FromSeconds(2)); return Task.FromResult(false); });
        Assert.Equal(new[] { 2000d }, Durations(logger, "ResetExecution"));
        Assert.Equal(new[] { 8000d }, Durations(logger, "Recreation"));
        Assert.Equal(new[] { 5000d }, Durations(logger, "Preparation"));
        Assert.Equal((1, 1), (pool.Snapshot.Ready, pool.Snapshot.Reserved));
    }

    [Fact]
    public async Task Reset_execution_excludes_permit_wait_and_failure_recovery()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var deleting = Signal(); var allowDeletion = Signal();
        var deletes = 0;
        await using var pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
            async _ =>
            {
                if (++deletes == 1) { deleting.SetResult(); await allowDeletion.Task; }
                if (deletes > 2) throw new InvalidOperationException("Unexpected repeated deletion.");
            }, timeProvider: clock, logger: logger);
        var lease = await pool.AcquireAsync();
        var returning = pool.ReturnAsync(lease, (_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            throw new IOException("private-reset-payload");
        });
        try
        {
            await deleting.Task;
            clock.Advance(TimeSpan.FromSeconds(5));
            allowDeletion.SetResult();
            var error = await Assert.ThrowsAsync<TurboTest.EFCore.TtLifecycleException>(() => returning);
            Assert.Contains("IOException", error.Message);
            Assert.Equal(new[] { 2000d }, Durations(logger, "ResetExecution"));
            Assert.Equal(new[] { 5000d }, Durations(logger, "RecoveryDeletion"));
            var reset = Assert.Single(logger.Records, record => Equals(record["Operation"], "ResetExecution"));
            Assert.Equal("Failed", reset["Outcome"]);
            Assert.DoesNotContain("private-reset", string.Join(" ", reset.Values));
        }
        finally
        {
            allowDeletion.TrySetResult();
            await Record.ExceptionAsync(() => returning);
        }
    }

    [Fact]
    public async Task Reset_permit_wait_is_measured_before_execution_begins()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var entered = Signal(); var release = Signal();
        await using var pool = new TtSlotPool<object>(2, 1, _ => new object(), (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask, timeProvider: clock, logger: logger);
        var one = await pool.AcquireAsync(); var two = await pool.AcquireAsync();
        var firstReturn = pool.ReturnAsync(one, async (_, _) => { entered.SetResult(); await release.Task; });
        await entered.Task;
        clock.Advance(TimeSpan.FromSeconds(2));
        var secondReturn = pool.ReturnAsync(two, (_, _) => { clock.Advance(TimeSpan.FromSeconds(7)); return Task.CompletedTask; });
        try
        {
            Assert.False(secondReturn.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(3));
            release.SetResult();
            await Task.WhenAll(firstReturn, secondReturn);
            Assert.Equal(new[] { 0d, 3000d }, Durations(logger, "ResetPermitWait"));
            Assert.Equal(new[] { 5000d, 7000d }, Durations(logger, "ResetExecution"));
            Assert.Equal((2, 0, 0), (pool.Snapshot.Ready, pool.Snapshot.WaitingResets, pool.Snapshot.ExecutingResets));
        }
        finally { release.TrySetResult(); await Task.WhenAll(firstReturn, secondReturn); }
    }

    [Fact]
    public async Task Queue_residence_and_preparation_permit_wait_are_distinct_from_execution()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var firstEntered = Signal(); var secondEntered = Signal();
        var firstRelease = Signal(); var secondRelease = Signal();
        var calls = 0;
        await using var pool = new TtSlotPool<object>(2, 1, _ => new object(), async (_, _) =>
        {
            switch (Interlocked.Increment(ref calls))
            {
                case 1: firstEntered.SetResult(); await firstRelease.Task; break;
                case 2: secondEntered.SetResult(); await secondRelease.Task; break;
                default: throw new InvalidOperationException("Unexpected extra preparation.");
            }
        }, _ => Task.CompletedTask, timeProvider: clock, logger: logger);
        var first = pool.AcquireAsync();
        await firstEntered.Task;
        var second = pool.AcquireAsync();
        try
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            firstRelease.SetResult();
            var one = await first;
            await secondEntered.Task;
            clock.Advance(TimeSpan.FromSeconds(3));
            secondRelease.SetResult();
            var two = await second;
            await Task.WhenAll(one.Settled.Task, two.Settled.Task);
            Assert.Equal(new[] { 2000d, 5000d }, Durations(logger, "QueueWait"));
            Assert.Equal(new[] { 0d, 2000d }, Durations(logger, "PreparationPermitWait"));
            Assert.Equal(new[] { 2000d, 3000d }, Durations(logger, "Preparation"));
            Assert.Equal((2, 2, 0), (pool.Snapshot.Reserved, pool.Snapshot.Leased, pool.Snapshot.QueuedAcquisitions));
            Assert.All(logger.Records, record => Assert.Equal(pool.Snapshot.OwnerId, record["OwnerId"]));
            await pool.ReturnAsync(one, (_, _) => Task.CompletedTask);
            await pool.ReturnAsync(two, (_, _) => Task.CompletedTask);
        }
        finally
        {
            firstRelease.TrySetResult(); secondRelease.TrySetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task Canceled_queue_observation_reports_its_residence_without_executing_preparation()
    {
        var clock = new ManualPoolClock();
        var logger = new FactoryTimingTests.TimingLogger();
        var preparations = 0;
        await using var pool = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) =>
        {
            if (++preparations > 1) throw new InvalidOperationException("Canceled waiter created capacity.");
            return Task.CompletedTask;
        }, _ => Task.CompletedTask, timeProvider: clock, logger: logger);
        var active = await pool.AcquireAsync();
        using var cancel = new CancellationTokenSource();
        var waiting = pool.AcquireAsync(cancel.Token);
        clock.Advance(TimeSpan.FromSeconds(4));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var record = Assert.Single(logger.Records, record => Equals(record["Operation"], "QueueWait") && Equals(record["Outcome"], "Canceled"));
        Assert.Equal(4000d, record["DurationMilliseconds"]);
        Assert.Equal(0, pool.Snapshot.QueuedAcquisitions);
        Assert.Equal(1, preparations);
        await pool.ReturnAsync(active, (_, _) => Task.CompletedTask);
    }

    internal static double[] Durations(FactoryTimingTests.TimingLogger logger, string operation) =>
        logger.Records.Where(record => Equals(record["Operation"], operation))
            .Select(record => Assert.IsType<double>(record["DurationMilliseconds"])).Order().ToArray();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class CancelOnTimestampClock(CancellationTokenSource cancellation) : TimeProvider
    {
        internal bool Armed { get; set; }
        public override long GetTimestamp()
        {
            if (Armed) { Armed = false; cancellation.Cancel(); }
            return 0;
        }
    }
}

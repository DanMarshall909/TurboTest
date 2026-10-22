using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CapacityMonitorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_stop_and_join_keep_cancellation_alive_until_reader_and_callbacks_settle_then_release_it(
        bool throwingCallback)
    {
        var readerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        var callbackFailure = new IOException("Private reader cancellation failure.");
        CancellationToken owned = default;
        CancellationTokenRegistration registration = default;
        WaitHandle? handle = null;
        var ticks = 0;
        var callbackCalls = 0;
        var monitor = new TtCapacityMonitor(TimeSpan.FromHours(1), TimeProvider.System, async token =>
        {
            owned = token;
            handle = token.WaitHandle;
            registration = token.Register(() =>
            {
                Interlocked.Increment(ref callbackCalls);
                callbackEntered.TrySetResult();
                releaseCallback.Wait();
                if (throwingCallback) throw callbackFailure;
            });
            readerEntered.TrySetResult();
            await releaseReader.Task;
            return null;
        }, _ => { ticks++; return Task.CompletedTask; });
        monitor.Start();
        try
        {
            await readerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AssertSourceAlive();
            monitor.Stop();
            var firstJoin = monitor.JoinAsync();
            monitor.Stop();
            var secondJoin = monitor.JoinAsync();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AssertSourceAlive();
            Assert.False(monitor.Completion.IsCompleted);
            releaseReader.TrySetResult();
            await monitor.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            AssertSourceAlive();
            Assert.False(firstJoin.IsCompleted);
            Assert.False(secondJoin.IsCompleted);
            releaseCallback.Set();
            await Task.WhenAll(firstJoin, secondJoin).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(handle!.SafeWaitHandle.IsClosed);
            Assert.Throws<ObjectDisposedException>(() => owned.WaitHandle);
            Assert.Equal((0, 1), (ticks, callbackCalls));
            var failures = new AggregateException(monitor.TakeFailures()).Flatten().InnerExceptions;
            if (throwingCallback) Assert.Same(callbackFailure, Assert.Single(failures));
            else Assert.Empty(failures);
            monitor.Stop();
            await monitor.JoinAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(monitor.TakeFailures());
            Assert.Equal(1, callbackCalls);
        }
        finally
        {
            releaseReader.TrySetResult();
            releaseCallback.Set();
            monitor.Stop();
            await monitor.JoinAsync();
            await Record.ExceptionAsync(() => monitor.Completion);
            registration.Dispose();
        }

        void AssertSourceAlive()
        {
            Assert.Same(handle, owned.WaitHandle);
            Assert.False(handle!.SafeWaitHandle.IsClosed);
        }
    }

    [Fact]
    public async Task Stop_cancels_the_owned_reader_and_joins_it_without_creating_new_tick_work()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 0;
        var monitor = new TtCapacityMonitor(TimeSpan.FromHours(1), TimeProvider.System, async token =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return null;
        }, _ => { ticks++; return Task.CompletedTask; });
        monitor.Start();
        try
        {
            Phase("observation.wait");
            Assert.False(monitor.Completion.IsCompleted);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Phase("observation.satisfied");
            monitor.Stop();
            await monitor.JoinAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(monitor.Completion.IsCompleted);
            Assert.Equal(0, ticks);
            Assert.Empty(monitor.TakeFailures());
            Assert.Throws<ObjectDisposedException>(monitor.Start);
        }
        catch (Exception error)
        {
            Phase("assertion.failed", error.GetType().Name);
            throw;
        }
        finally
        {
            Phase("cleanup.join.started");
            monitor.Stop();
            await monitor.JoinAsync();
            Phase("cleanup.join.settled");
        }
    }

    [Fact]
    public async Task Stop_keeps_a_reader_that_ignores_cancellation_owned_until_it_actually_returns()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 0;
        var monitor = new TtCapacityMonitor(TimeSpan.FromHours(1), TimeProvider.System, async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            return null;
        }, _ => { ticks++; return Task.CompletedTask; });
        monitor.Start();
        try
        {
            Phase("reader.wait");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Phase("reader.claimed");
            monitor.Stop();
            var joining = monitor.JoinAsync();
            Assert.False(joining.IsCompleted);
            Assert.False(monitor.Completion.IsCompleted);
            release.TrySetResult();
            await joining.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(monitor.Completion.IsCompleted);
            Assert.Equal(0, ticks);
            Assert.Empty(monitor.TakeFailures());
        }
        catch (Exception error)
        {
            Phase("assertion.failed", error.GetType().Name);
            throw;
        }
        finally
        {
            release.TrySetResult();
            monitor.Stop();
            Phase("cleanup.join.started");
            await monitor.JoinAsync();
            // Preserve the assertion failure, while still joining the actual
            // callback even if a diagnostic fault makes JoinAsync return early.
            await Record.ExceptionAsync(async () => await monitor.Completion);
            Phase("cleanup.join.settled");
        }
    }

    [Fact]
    public async Task Stop_does_not_abandon_a_claimed_tick_and_its_late_material_failure_is_retained_once()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Private backend material.");
        var reading = new TtMemoryReading(17, 99, 37);
        var monitor = new TtCapacityMonitor(TimeSpan.FromHours(1), TimeProvider.System,
            _ => Task.FromResult<TtMemoryReading?>(reading), async observed =>
            {
                Assert.Same(reading, observed);
                entered.TrySetResult();
                await release.Task;
                throw failure;
            });
        monitor.Start();
        try
        {
            Assert.False(monitor.Completion.IsCompleted);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            monitor.Stop();
            var joining = monitor.JoinAsync();
            Assert.False(joining.IsCompleted);
            release.TrySetResult();
            await joining.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(failure, Assert.Single(monitor.TakeFailures()));
            await monitor.JoinAsync();
            Assert.Empty(monitor.TakeFailures());
        }
        finally { release.TrySetResult(); monitor.Stop(); await monitor.JoinAsync(); }
    }

    [Fact]
    public async Task Sampling_failure_passes_unavailable_data_without_claiming_a_material_maintenance_failure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new TtCapacityMonitor(TimeSpan.FromHours(1), TimeProvider.System,
            _ => Task.FromException<TtMemoryReading?>(new IOException("Unavailable statistics.")), observed =>
            {
                Assert.Null(observed);
                entered.TrySetResult();
                return Task.CompletedTask;
            });
        monitor.Start();
        try
        {
            Assert.False(monitor.Completion.IsCompleted);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { monitor.Stop(); await monitor.JoinAsync(); }
        Assert.Empty(monitor.TakeFailures());
    }

    private static void Phase(string phase, string? exceptionType = null,
        [System.Runtime.CompilerServices.CallerMemberName] string test = "")
    {
        var path = Environment.GetEnvironmentVariable("TURBOTEST_MONITOR_PHASE_LOG");
        if (string.IsNullOrEmpty(path)) return;
        // Closing each append preserves the observation failure even if teardown
        // stalls and the diagnostic process is terminated by its external guard.
        File.AppendAllText(path, System.Text.Json.JsonSerializer.Serialize(new { test, phase, exceptionType }) + "\n");
    }
}

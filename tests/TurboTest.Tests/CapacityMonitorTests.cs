using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class CapacityMonitorTests
{
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
            Assert.False(monitor.Completion.IsCompleted);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            monitor.Stop();
            await monitor.JoinAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(monitor.Completion.IsCompleted);
            Assert.Equal(0, ticks);
            Assert.Empty(monitor.TakeFailures());
            Assert.Throws<ObjectDisposedException>(monitor.Start);
        }
        finally { monitor.Stop(); await monitor.JoinAsync(); }
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
}

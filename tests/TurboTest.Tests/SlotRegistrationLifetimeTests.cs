using System.Runtime.CompilerServices;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotRegistrationLifetimeTests
{
    [Theory]
    [InlineData("Published")]
    [InlineData("Closed")]
    [InlineData("Canceled")]
    public async Task Settled_acquisition_releases_its_pool_while_the_caller_cancellation_source_is_still_held(string completion)
    {
        using var caller = new CancellationTokenSource();
        var repeats = completion == "Canceled" ? 1 : 16;
        var pools = await Task.Run(() => Enumerable.Range(0, repeats).Select(_ => CompleteAcquisition(caller, completion)).ToArray());
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.All(pools, pool => Assert.False(pool.IsAlive));
        Assert.Equal(completion == "Canceled", caller.IsCancellationRequested);
        GC.KeepAlive(caller);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CompleteAcquisition(CancellationTokenSource caller, string completion)
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var deleted = 0;
        var pool = new TtSlotPool<object>(1, 1, _ =>
        {
            if (++created > 1) throw new InvalidOperationException("Acquisition created an extra reservation.");
            return new object();
        }, async (_, _) =>
        {
            preparing.TrySetResult();
            await release.Task;
        }, _ =>
        {
            if (++deleted > 1) throw new InvalidOperationException("Completed deletion was repeated.");
            return Task.CompletedTask;
        });
        var acquiring = pool.AcquireAsync(caller.Token);
        try
        {
            preparing.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Assert.False(acquiring.IsCompleted);
            Assert.Equal(1, pool.Snapshot.QueuedAcquisitions);
            if (completion == "Published")
            {
                release.TrySetResult();
                acquiring.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                Assert.Equal(1, pool.Snapshot.Leased);
            }
            else
            {
                if (completion == "Canceled")
                {
                    caller.Cancel();
                    Assert.ThrowsAny<OperationCanceledException>(() => acquiring.GetAwaiter().GetResult());
                }
                else Assert.Equal("Closed", completion);
                var drain = pool.DisposeAsync().AsTask();
                Assert.False(drain.IsCompleted);
                if (completion == "Closed")
                    Assert.Throws<ObjectDisposedException>(() => acquiring.GetAwaiter().GetResult());
                release.TrySetResult();
                drain.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            }
            pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            Assert.Equal((1, 1, 0, 0), (created, deleted, pool.Snapshot.Reserved, pool.Snapshot.QueuedAcquisitions));
            return new WeakReference(pool);
        }
        finally
        {
            release.TrySetResult();
            pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}

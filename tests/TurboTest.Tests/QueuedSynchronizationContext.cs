namespace TurboTest.Tests;

/// <summary>Holds continuations so lifecycle tests can establish an explicit ordering.</summary>
internal sealed class QueuedSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _work = new();
    private bool _released;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        lock (_work)
        {
            if (!_released)
            {
                _work.Enqueue((callback, state));
                Monitor.Pulse(_work);
                return;
            }
        }
        ThreadPool.QueueUserWorkItem(_ => callback(state));
    }

    internal void RunUntil(Task entered)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!entered.IsCompleted)
            TakeNext(TimeSpan.FromSeconds(2) - timer.Elapsed)();
    }

    internal Action TakeNext() => TakeNext(TimeSpan.FromSeconds(2));

    internal void RunPostedCallbacks()
    {
        var fence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => fence.TrySetResult(), null);
        RunUntil(fence.Task);
    }

    private Action TakeNext(TimeSpan deadline)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        (SendOrPostCallback Callback, object? State) item;
        lock (_work)
        {
            while (_work.Count == 0)
            {
                var remaining = deadline - timer.Elapsed;
                if (remaining <= TimeSpan.Zero || !Monitor.Wait(_work, remaining))
                    throw new TimeoutException("Expected lifecycle continuation was not posted within its observation deadline.");
            }
            item = _work.Dequeue();
        }
        return () => item.Callback(item.State);
    }

    internal void ReleaseToThreadPool()
    {
        lock (_work)
        {
            _released = true;
            while (_work.TryDequeue(out var item))
            {
                var pending = item;
                ThreadPool.QueueUserWorkItem(_ => pending.Callback(pending.State));
            }
        }
    }
}

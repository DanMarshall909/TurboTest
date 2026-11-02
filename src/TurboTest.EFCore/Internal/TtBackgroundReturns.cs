namespace TurboTest.EFCore.Internal;

/// <summary>Owns complete return operations, including diagnostic delivery, after lease handoff.</summary>
internal sealed class TtBackgroundReturns
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private readonly List<TtLifecycleException> _failures = [];
    private Task? _drain;

    internal void Start(Func<Task> operation)
    {
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_drain is not null, this);
            _pending.Add(settled.Task);
        }
        _ = CompleteAsync(operation, settled);
    }

    private async Task CompleteAsync(Func<Task> operation, TaskCompletionSource settled)
    {
        try { await operation(); }
        catch (Exception error)
        {
            lock (_gate) _failures.Add(error as TtLifecycleException ?? new TtLifecycleException("BackgroundReset", error));
        }
        finally
        {
            lock (_gate)
            {
                settled.TrySetResult();
                _pending.Remove(settled.Task);
            }
        }
    }

    internal Task DrainAsync()
    {
        lock (_gate) return _drain ??= Task.WhenAll(_pending);
    }

    internal IReadOnlyList<TtLifecycleException> TakeFailures()
    {
        lock (_gate)
        {
            var failures = _failures.ToArray();
            _failures.Clear();
            return failures;
        }
    }
}

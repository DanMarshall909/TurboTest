namespace TurboTest.EFCore.Internal;

/// <summary>Owns sampling and admitted maintenance until both actually settle.</summary>
internal sealed class TtCapacityMonitor
{
    private readonly object _gate = new();
    private readonly TimeSpan _interval;
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, Task<TtMemoryReading?>> _read;
    private readonly Func<TtMemoryReading?, Task> _tick;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Exception> _failures = [];
    private Task? _task;
    private Task _cancellation = Task.CompletedTask;
    private bool _stopping;
    private bool _terminalObserved;

    internal TtCapacityMonitor(TimeSpan interval, TimeProvider clock,
        Func<CancellationToken, Task<TtMemoryReading?>> read, Func<TtMemoryReading?, Task> tick)
    { _interval = interval; _clock = clock; _read = read; _tick = tick; }

    internal Task Completion { get { lock (_gate) return _task ?? Task.CompletedTask; } }

    internal void Start()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_task is not null) throw new InvalidOperationException("Capacity monitoring is already started.");
            _task = RunAsync(start.Task);
        }
        start.TrySetResult();
    }

    internal void Stop()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            _cancellation = _stop.CancelAsync();
        }
    }

    internal async Task JoinAsync()
    {
        Stop();
        Task running, cancellation;
        lock (_gate) { running = _task ?? Task.CompletedTask; cancellation = _cancellation; }
        try { await Task.WhenAll(running, cancellation); }
        catch { /* Retain terminal failures below after both actual jobs settle. */ }
        lock (_gate)
        {
            if (_terminalObserved) return;
            _terminalObserved = true;
            if (running.Exception is { } runFailure) _failures.AddRange(runFailure.InnerExceptions);
            if (cancellation.Exception is { } cancelFailure) _failures.AddRange(cancelFailure.InnerExceptions);
            _stop.Dispose();
        }
    }

    internal Exception[] TakeFailures()
    {
        lock (_gate) { var failures = _failures.ToArray(); _failures.Clear(); return failures; }
    }

    private async Task RunAsync(Task start)
    {
        await start;
        var token = _stop.Token;
        while (!token.IsCancellationRequested)
        {
            TtMemoryReading? memory;
            try { memory = await _read(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception) { memory = null; }
            if (token.IsCancellationRequested) return;
            Task? tick = null;
            try { tick = _tick(memory); await tick; }
            catch (Exception error)
            {
                var errors = tick?.Exception?.InnerExceptions.ToArray() ?? [error];
                lock (_gate)
                    _failures.AddRange(errors.Where(failure => !token.IsCancellationRequested ||
                        failure is not (OperationCanceledException or ObjectDisposedException)));
                return;
            }
            try { await Task.Delay(_interval, _clock, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
}

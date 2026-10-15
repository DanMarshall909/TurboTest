using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>Owns one test database and all its contexts until asynchronous return completes.</summary>
public sealed class TtLease<TContext> : IAsyncDisposable where TContext : DbContext
{
    private readonly object _gate = new();
    private readonly Func<TContext> _createContext;
    private readonly Func<Task> _return;
    private readonly Action<TContext> _register;
    private readonly TContext _context;
    private readonly TimeProvider _clock;
    private readonly Action<TimeSpan, string>? _observeReturn;
    private Task? _disposal;
    private bool _closing;

    internal TtLease(TContext context, Func<TContext> createContext,
        Action<TContext> register, Func<Task> returnDatabase, TimeProvider? timeProvider = null,
        Action<TimeSpan, string>? observeReturn = null)
    {
        _context = context;
        _createContext = createContext;
        _register = register;
        _return = returnDatabase;
        _clock = timeProvider ?? TimeProvider.System;
        _observeReturn = observeReturn;
    }

    public TContext Context
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                return _context;
            }
        }
    }

    /// <summary>Creates another context in this test's database. The lease retains cleanup ownership.</summary>
    public TContext CreateContext()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var context = _createContext();
            _register(context);
            return context;
        }
    }

    public ValueTask DisposeAsync()
    {
        Task disposal;
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            _closing = true;
            if (_disposal is null)
            {
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = ReturnAsync(start.Task, _clock.GetTimestamp());
            }
            disposal = _disposal;
        }
        start?.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task ReturnAsync(Task start, long requested)
    {
        await start;
        var outcome = "Succeeded";
        try { await _return(); }
        catch (Exception error) { outcome = TtTelemetry.Outcome(error); throw; }
        finally
        {
            try { _observeReturn?.Invoke(_clock.GetElapsedTime(requested), outcome); }
            catch (Exception) { /* Observers cannot replace the shared return result. */ }
        }
    }
}

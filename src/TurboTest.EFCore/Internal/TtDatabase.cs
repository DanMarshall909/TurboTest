namespace TurboTest.EFCore.Internal;

/// <summary>
/// Represents a database instance that can be used for testing.
/// </summary>
internal sealed class TtDatabase : IAsyncDisposable
{
    private readonly Func<Task> _returnDatabase;
    private readonly Func<Task> _discardDatabase;
    private readonly List<Func<Task>> _disposeCallbacks = new();
    private bool _closing;
    private Task? _disposal;

    public Npgsql.NpgsqlDataSource DataSource { get; }

    internal TtDatabase(Npgsql.NpgsqlDataSource dataSource, Func<Task> returnDatabase, Func<Task> discardDatabase)
    {
        DataSource = dataSource;
        _returnDatabase = returnDatabase;
        _discardDatabase = discardDatabase;
    }

    /// <summary>
    /// Register a callback to be executed before the database is returned to the pool
    /// </summary>
    internal void OnDispose(Func<Task> callback)
    {
        lock (_disposeCallbacks)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            _disposeCallbacks.Add(callback);
        }
    }

    public ValueTask DisposeAsync() => FinishAsync(discard: false);

    internal ValueTask DiscardAsync() => FinishAsync(discard: true);

    private ValueTask FinishAsync(bool discard)
    {
        Task disposal;
        TaskCompletionSource? start = null;
        lock (_disposeCallbacks)
        {
            _closing = true;
            if (_disposal is null)
            {
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposal = DisposeCoreAsync(discard, start.Task);
            }
            disposal = _disposal;
        }
        start?.TrySetResult();
        return new ValueTask(disposal);
    }

    private async Task DisposeCoreAsync(bool discard, Task start)
    {
        await start;
        // Execute all dispose callbacks first (EF Core context disposal)
        List<Func<Task>> callbacks;
        lock (_disposeCallbacks)
        {
            callbacks = _disposeCallbacks.ToList();
        }

        Exception? cleanupFailure = null;
        foreach (var callback in callbacks)
        {
            try
            {
                await callback();
            }
            catch (Exception error)
            {
                // Preserve the original failure even if later cleanup also fails.
                cleanupFailure ??= error;
            }
        }

        if (cleanupFailure is not null)
        {
            var failure = new TtLifecycleException("LeaseCleanup", cleanupFailure);
            try { await _discardDatabase(); }
            catch (Exception error) { failure = failure.WithCleanupFailure(error); }
            throw failure;
        }

        // Now return to pool (which will reset the database)
        if (discard) await _discardDatabase();
        else await _returnDatabase();
    }
}

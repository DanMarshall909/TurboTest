using Microsoft.EntityFrameworkCore;

namespace TurboTest.EFCore.Internal;

internal sealed class TtScopedLease<TContext>(ITtServiceFactory factory,
    Action<DbContextOptionsBuilder>? configure) : IAsyncDisposable where TContext : DbContext
{
    private readonly object _gate = new();
    private Task<TtLease<TContext>>? _initialization;
    private bool _closing;

    internal Task InitializeAsync()
    {
        Task initialization;
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_initialization is null)
            {
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _initialization = InitializeCoreAsync(start.Task);
            }
            initialization = _initialization;
        }
        start?.TrySetResult();
        return initialization;
    }

    private async Task<TtLease<TContext>> InitializeCoreAsync(Task start)
    {
        await start;
        return await factory.CreateLeaseAsync<TContext>(configure);
    }

    internal TtLease<TContext> Lease
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closing, this);
                if (_initialization?.IsCompletedSuccessfully != true)
                    throw new InvalidOperationException("Await scope.ServiceProvider.InitializeTurboTestAsync<TContext>() before resolving TtLease<TContext>. Use CreateAsyncScope and DisposeAsync.");
                // The completed-success check above makes this a nonblocking read.
                return _initialization.Result;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task<TtLease<TContext>>? initialization;
        lock (_gate) { _closing = true; initialization = _initialization; }
        if (initialization is not null) await (await initialization).DisposeAsync();
    }
}

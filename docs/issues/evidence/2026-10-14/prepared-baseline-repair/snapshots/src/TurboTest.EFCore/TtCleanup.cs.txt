using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>Retains failed-startup cleanup ownership without exposing a database or credentials.</summary>
public sealed class TtCleanup
{
    private readonly object _gate = new();
    private Func<Task>? _cleanup;
    private Func<IReadOnlyList<string>>? _resources;
    private Task? _attempt;

    public Guid OwnerId { get; }
    public IReadOnlyList<string> OwnedResourceIds
    {
        get
        {
            lock (_gate)
                return _resources is null ? Array.Empty<string>() : Array.AsReadOnly(_resources().ToArray());
        }
    }

    internal TtCleanup(Guid ownerId, Func<Task> cleanup, Func<IReadOnlyList<string>> resources)
    {
        OwnerId = ownerId;
        _cleanup = cleanup;
        _resources = resources;
    }

    /// <summary>Shares a pending attempt; a completed failure can be explicitly retried.</summary>
    public Task RetryCleanupAsync()
    {
        lock (_gate)
        {
            if (_attempt is null || _attempt.IsFaulted)
                _attempt = RunAsync();
            return _attempt;
        }
    }

    private async Task RunAsync()
    {
        try { await _cleanup!(); }
        catch (Exception error)
        {
            var failure = error as TtLifecycleException ?? new TtLifecycleException("FailedStartupCleanup", error);
            throw failure.WithCleanup(this);
        }
        lock (_gate)
        {
            _cleanup = null;
            _resources = null;
        }
        TtCleanupRegistry.Release(this);
    }
}

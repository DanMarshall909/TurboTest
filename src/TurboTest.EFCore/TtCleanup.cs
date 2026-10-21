using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>Retains failed-startup cleanup ownership without exposing a database or credentials.</summary>
public sealed class TtCleanup
{
    private readonly object _gate = new();
    private Func<Task>? _cleanup;
    private Func<IReadOnlyList<string>>? _resources;
    private Task? _attempt;
    private TtCleanup[]? _members;

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

    internal static TtCleanup? Combine(IEnumerable<TtCleanup?> cleanups)
    {
        var supplied = cleanups.OfType<TtCleanup>().ToArray();
        var owners = supplied.SelectMany(owner => owner.Members()).Where(owner => owner.HasWork).Distinct().ToArray();
        if (owners.Length == 0) return null;
        if (owners.Length == 1) return owners[0];
        var covered = owners.ToHashSet();
        foreach (var owner in supplied)
            if (owner.HasWork && covered.SetEquals(owner.Members().Where(member => member.HasWork))) return owner;
        // A group forwards recovery; only the actual owners retain registry entries.
        return new TtCleanup(Guid.NewGuid(), async () =>
        {
            var pending = Task.WhenAll(owners.Select(owner => owner.RetryCleanupAsync()));
            try { await pending; }
            catch when (pending.Exception is not null)
            {
                throw new TtLifecycleException("RetainedCleanup", pending.Exception);
            }
        }, () => owners.SelectMany(owner => owner.OwnedResourceIds).Distinct(StringComparer.Ordinal).ToArray())
        { _members = owners };
    }

    private TtCleanup[] Members()
    {
        lock (_gate) return _members ?? [this];
    }

    private bool HasWork
    {
        get { lock (_gate) return _cleanup is not null; }
    }

    /// <summary>Shares a pending attempt; a completed failure can be explicitly retried.</summary>
    public Task RetryCleanupAsync()
    {
        Task attempt;
        TaskCompletionSource? start = null;
        lock (_gate)
        {
            if (_attempt is null || _attempt.IsFaulted)
            {
                start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _attempt = RunAsync(start.Task);
            }
            attempt = _attempt;
        }
        start?.TrySetResult();
        return attempt;
    }

    private async Task RunAsync(Task start)
    {
        await start;
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
            _members = null;
        }
        TtCleanupRegistry.Release(this);
    }
}

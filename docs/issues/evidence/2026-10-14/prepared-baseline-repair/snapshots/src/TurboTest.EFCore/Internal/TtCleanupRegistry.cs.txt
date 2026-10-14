using System.Collections.Concurrent;

namespace TurboTest.EFCore.Internal;

/// <summary>Retains failed startup owners even when a caller no longer holds the exception.</summary>
internal static class TtCleanupRegistry
{
    private static readonly ConcurrentDictionary<Guid, TtCleanup> FailedOwners = new();

    internal static TtCleanup Retain(Guid owner, Func<Task> cleanup, Func<IReadOnlyList<string>> resources)
    {
        var handle = new TtCleanup(owner, cleanup, resources);
        if (!FailedOwners.TryAdd(owner, handle))
            throw new InvalidOperationException("The failed startup owner is already retained.");
        return handle;
    }

    internal static void Release(TtCleanup cleanup) =>
        FailedOwners.TryRemove(new KeyValuePair<Guid, TtCleanup>(cleanup.OwnerId, cleanup));

    internal static async Task DrainAsync()
    {
        Exception? first = null;
        foreach (var cleanup in FailedOwners.Values)
        {
            try { await cleanup.RetryCleanupAsync(); }
            catch (Exception error) { first ??= error; }
        }
        if (first is not null) throw first;
    }
}

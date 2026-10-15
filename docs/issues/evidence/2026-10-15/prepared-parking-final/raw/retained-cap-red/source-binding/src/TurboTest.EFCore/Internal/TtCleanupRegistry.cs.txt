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

    internal static async Task DrainAsync(TimeSpan timeout)
    {
        async Task<Exception?> ObserveAsync(TtCleanup cleanup)
        {
            var attempt = cleanup.RetryCleanupAsync();
            try { await attempt.WaitAsync(timeout); return null; }
            catch (TimeoutException error) when (!attempt.IsCompleted)
            {
                return new TtLifecycleException("DrainTimeout", error, cleanup: cleanup);
            }
            catch (Exception error) { return error; }
        }
        var errors = await Task.WhenAll(FailedOwners.Values.Select(ObserveAsync));
        var first = errors.FirstOrDefault(error => error is not null);
        if (first is not null) throw first;
    }
}

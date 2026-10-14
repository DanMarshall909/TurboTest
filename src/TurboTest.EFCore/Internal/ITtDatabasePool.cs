namespace TurboTest.EFCore.Internal;

internal interface ITtDatabasePool : IAsyncDisposable
{
    TtPoolSnapshot Snapshot { get; }
    TtResourceBudget Budget { get; }
    IReadOnlyList<string> OwnedResourceIds { get; }
    Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
    Task RetryCleanupAsync() => DisposeAsync().AsTask();
}

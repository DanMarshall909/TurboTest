namespace TurboTest.EFCore.Internal;

internal interface ITtDatabasePool : IAsyncDisposable
{
    TtPoolSnapshot Snapshot { get; }
    TtResourceBudget Budget { get; }
    IReadOnlyList<string> OwnedResourceIds { get; }
    Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
    Task MaintainIdleAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Idle parking requires a prepared baseline fixture.");
    // Stop admission/recovery before public lease cleanup; physical drain follows it.
    void StopAcceptingWork() { }
    Task RetryCleanupAsync() => DisposeAsync().AsTask();
}

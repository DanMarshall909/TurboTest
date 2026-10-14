namespace TurboTest.EFCore.Internal;

internal interface ITtDatabasePool : IAsyncDisposable
{
    TtPoolSnapshot Snapshot { get; }
    TtResourceBudget Budget { get; }
    Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default);
}

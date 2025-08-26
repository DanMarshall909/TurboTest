namespace TurboTest.EFCore.Internal;

/// <summary>
/// EF Core interceptor that handles database cleanup when the context is disposed.
/// Coordinates with database reset to avoid connection conflicts.
/// </summary>
internal class TtCleanupInterceptor : IAsyncDisposable
{
    private readonly TtDatabase _database;

    public TtCleanupInterceptor(TtDatabase database)
    {
        _database = database;
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
    }
}
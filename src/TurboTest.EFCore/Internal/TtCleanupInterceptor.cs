using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// EF Core interceptor that handles database cleanup when the context is disposed.
/// </summary>
internal class TtCleanupInterceptor : IDbConnectionInterceptor, IAsyncDisposable
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
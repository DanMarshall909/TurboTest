using Microsoft.EntityFrameworkCore;

namespace TurboTest.EFCore.Internal;

/// <summary>
/// Wrapper for DbContext that handles database cleanup on disposal
/// </summary>
internal class TtContext<T> : IAsyncDisposable where T : DbContext
{
    private readonly T _context;
    private readonly TtDatabase _database;

    public TtContext(T context, TtDatabase database)
    {
        _context = context;
        _database = database;
        
        // Register EF Core context disposal as a callback
        _database.OnDispose(async () => await _context.DisposeAsync());
    }

    public T Context => _context;

    // Implicit conversion to the underlying context
    public static implicit operator T(TtContext<T> wrapper) => wrapper._context;

    public async ValueTask DisposeAsync()
    {
        // Database will handle EF Core context disposal via callback, then reset
        await _database.DisposeAsync();
    }
}
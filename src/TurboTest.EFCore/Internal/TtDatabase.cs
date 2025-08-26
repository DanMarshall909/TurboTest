namespace TurboTest.EFCore.Internal;

/// <summary>
/// Represents a database instance that can be used for testing.
/// </summary>
internal sealed class TtDatabase : IAsyncDisposable
{
    private readonly TtPool _pool;
    private readonly TtContainer _container;
    private bool _disposed;

    public string ConnectionString => _container.ConnectionString;

    internal TtDatabase(TtPool pool, TtContainer container)
    {
        _pool = pool;
        _container = container;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _pool.ReleaseAsync(_container);
    }
}
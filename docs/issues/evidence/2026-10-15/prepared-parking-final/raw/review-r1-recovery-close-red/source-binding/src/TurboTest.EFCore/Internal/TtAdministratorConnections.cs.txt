using Npgsql;

namespace TurboTest.EFCore.Internal;

/// <summary>Owns the allowance for every prepared control and verification connection.</summary>
internal sealed class TtAdministratorConnections(int capacity, Func<string?, NpgsqlConnection> create)
{
    internal sealed class Lease(NpgsqlConnection connection, Action release) : IAsyncDisposable
    {
        private readonly object _gate = new();
        private Task? _disposal;
        internal NpgsqlConnection Connection { get; } = connection;

        public ValueTask DisposeAsync()
        {
            lock (_gate) return new ValueTask(_disposal ??= CloseAsync());
        }

        private async Task CloseAsync()
        {
            try { await Connection.DisposeAsync(); }
            finally { release(); }
        }
    }

    private readonly SemaphoreSlim _limit = new(capacity, capacity);
    private readonly object _gate = new();
    private int _active;
    private int _waiting;

    internal (int Active, int Waiting) Snapshot
    {
        get { lock (_gate) return (_active, _waiting); }
    }

    internal async Task<Lease> OpenAsync(string? database = null, CancellationToken cancellationToken = default)
    {
        lock (_gate) _waiting++;
        try { await _limit.WaitAsync(cancellationToken); }
        finally { lock (_gate) _waiting--; }
        lock (_gate) _active++;
        NpgsqlConnection? connection = null;
        try
        {
            connection = create(database);
            await connection.OpenAsync(cancellationToken);
            return new Lease(connection, Release);
        }
        catch
        {
            try { if (connection is not null) await connection.DisposeAsync(); }
            finally { Release(); }
            throw;
        }
    }

    private void Release()
    {
        lock (_gate) _active--;
        _limit.Release();
    }
}

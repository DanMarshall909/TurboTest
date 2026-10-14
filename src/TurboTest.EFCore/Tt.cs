using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TurboTest.EFCore.Internal;

namespace TurboTest.EFCore;

/// <summary>Creates fixture-owned leases and provides an explicitly disposable static convenience owner.</summary>
public static class Tt
{
    private static readonly SemaphoreSlim OwnerGate = new(1, 1);
    private static readonly object ConfigurationGate = new();
    private static readonly Dictionary<Type, IAsyncDisposable> Owners = new();
    private static TtOptions _options = new();
    private static bool _started;

    /// <summary>Configure before the first static lease, or after awaiting static disposal.</summary>
    public static void Configure(IConfiguration configuration) =>
        Configure(options => configuration.GetSection(TtOptions.SectionName).Bind(options));

    public static void Configure(Action<TtOptions> configure)
    {
        lock (ConfigurationGate)
        {
            if (_started) throw new InvalidOperationException("Await Tt.DisposeAsync before changing static configuration.");
            var options = _options.Snapshot();
            configure(options);
            _options = options.Snapshot();
        }
    }

    public static async Task<TtLease<TContext>> CreateLeaseAsync<TContext>(
        Action<DbContextOptionsBuilder>? configure = null, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        TtFactory<TContext> factory;
        await OwnerGate.WaitAsync(cancellationToken);
        try
        {
            if (Owners.TryGetValue(typeof(TContext), out var owner)) factory = (TtFactory<TContext>)owner;
            else
            {
                TtOptions options;
                lock (ConfigurationGate) { _started = true; options = _options.Snapshot(); }
                factory = await CreateOwnedFactoryAsync<TContext>(options, cancellationToken);
                Owners.Add(typeof(TContext), factory);
            }
        }
        finally { OwnerGate.Release(); }
        return await factory.CreateLeaseAsync(configure, cancellationToken);
    }

    /// <summary>Creates an independent fixture. Factory disposal closes its leases and owned pool.</summary>
    public static Task<TtFactory<TContext>> CreateFactoryAsync<TContext>(
        Action<TtOptions>? configure = null, CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var options = new TtOptions();
        configure?.Invoke(options);
        return CreateOwnedFactoryAsync<TContext>(options.Snapshot(), cancellationToken);
    }

    private static async Task<TtFactory<TContext>> CreateOwnedFactoryAsync<TContext>(
        TtOptions options, CancellationToken cancellationToken) where TContext : DbContext
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pool = new TtPool(options);
        try
        {
            await pool.InitializeAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return new TtFactory<TContext>(pool, options, ownsPool: true);
        }
        catch
        {
            await pool.DisposeAsync();
            throw;
        }
    }

    /// <summary>Closes static leases and resources. A later create starts a new owner.</summary>
    public static async Task DisposeAsync()
    {
        await OwnerGate.WaitAsync();
        try
        {
            Exception? first = null;
            foreach (var owner in Owners.Values)
            {
                try { await owner.DisposeAsync(); }
                catch (Exception error) { first ??= error; }
            }
            Owners.Clear();
            lock (ConfigurationGate) _started = false;
            if (first is not null) throw first;
        }
        finally { OwnerGate.Release(); }
    }
}

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

    public static async Task<TtFactory<TContext>> CreateFactoryAsync<TContext>(
        TtBaselineDefinition<TContext> baseline, Action<TtOptions>? configure = null,
        CancellationToken cancellationToken = default) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var options = new TtOptions();
        configure?.Invoke(options);
        options = options.Snapshot();
        return await CreatePreparedFactoryAsync(options, baseline, cancellationToken);
    }

    internal static async Task<TtFactory<TContext>> CreatePreparedFactoryAsync<TContext>(
        TtOptions options, TtBaselineDefinition<TContext> baseline, CancellationToken cancellationToken,
        Func<TtContainer, Task>? deleteHost = null, Func<Npgsql.NpgsqlDataSource, Task>? beforeVerification = null,
        Func<string, Task>? prepareImage = null,
        Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task>? afterSequenceRestore = null,
        Func<Npgsql.NpgsqlConnection, Task>? afterRowCommit = null)
        where TContext : DbContext
    {
        var pool = new TtPreparedPool<TContext>(options, baseline, beforeVerification, deleteHost, prepareImage,
            afterSequenceRestore, afterRowCommit);
        try
        {
            await pool.InitializeAsync(cancellationToken);
            return new TtFactory<TContext>(pool, options, ownsPool: true,
                preparedOptions: pool.BuildOptions, validateContext: pool.ValidateContext);
        }
        catch (Exception error)
        {
            Exception? cleanupFailure = null;
            try { await pool.DisposeAsync(); }
            catch (Exception cleanupError) { cleanupFailure = cleanupError; }
            if (error is OperationCanceledException && cleanupFailure is null) throw;
            var failure = error as TtLifecycleException ?? new TtLifecycleException("BaselineInitialize", error);
            if (cleanupFailure is not null)
            {
                var cleanup = TtCleanupRegistry.Retain(pool.OwnerId, pool.RetryCleanupAsync, () => pool.OwnedResourceIds);
                failure = new TtLifecycleException(failure.Stage, failure, cleanupFailure, cleanup);
            }
            throw failure;
        }
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
            try { await TtCleanupRegistry.DrainAsync(); }
            catch (Exception error) { first ??= error; }
            lock (ConfigurationGate) _started = false;
            if (first is not null) throw first;
        }
        finally { OwnerGate.Release(); }
    }
}

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

    public static Task<TtLease<TContext>> CreateLeaseAsync<TContext>(
        Action<DbContextOptionsBuilder>? configure = null, CancellationToken cancellationToken = default)
        where TContext : DbContext
        => CreateLeaseAsync<TContext>(configure, cancellationToken, null);

    internal static async Task<TtLease<TContext>> CreateLeaseAsync<TContext>(
        Action<DbContextOptionsBuilder>? configure, CancellationToken cancellationToken,
        Func<TtContainer, Task>? deleteResource, Func<TtContainer, Task>? beforeStart = null) where TContext : DbContext
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
                factory = await CreateOwnedFactoryAsync<TContext>(options, cancellationToken, beforeStart, deleteResource);
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
        Func<Npgsql.NpgsqlConnection, Task>? afterRowCommit = null,
        Func<Npgsql.NpgsqlConnection, string, Task>? dropDatabase = null, TimeProvider? timeProvider = null)
        where TContext : DbContext
    {
        var pool = new TtPreparedPool<TContext>(options, baseline, beforeVerification, deleteHost, prepareImage,
            afterSequenceRestore, afterRowCommit, dropDatabase, timeProvider);
        await InitializeOwnedPoolAsync(pool, options, pool.InitializeAsync, "BaselineInitialize", cancellationToken);
        return new TtFactory<TContext>(pool, options, ownsPool: true,
            preparedOptions: pool.BuildOptions, validateContext: pool.ValidateContext, timeProvider: timeProvider);
    }

    internal static async Task<TtFactory<TContext>> CreateOwnedFactoryAsync<TContext>(
        TtOptions options, CancellationToken cancellationToken, Func<TtContainer, Task>? beforeStart = null,
        Func<TtContainer, Task>? deleteResource = null) where TContext : DbContext
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pool = new TtPool(options, deleteResource, beforeStart: beforeStart);
        await InitializeOwnedPoolAsync(pool, options, pool.InitializeAsync, "ContainerInitialize", cancellationToken);
        return new TtFactory<TContext>(pool, options, ownsPool: true);
    }

    internal static async Task InitializeOwnedPoolAsync(ITtDatabasePool pool, TtOptions options,
        Func<CancellationToken, Task> initialize, string stage, CancellationToken cancellationToken)
    {
        var initializationCancellation = new CancellationTokenSource();
        var cleanupOwnsCancellation = false;
        var initialization = initialize(initializationCancellation.Token);
        try
        {
            await initialization.WaitAsync(options.StartupTimeout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            var interrupted = cancellationToken.IsCancellationRequested ||
                (error is TimeoutException && !ReferenceEquals(initialization.Exception?.InnerException, error));
            TtLifecycleException? initializationFailure = null;
            Exception? cancellationFailure = null;
            cleanupOwnsCancellation = true;
            var cancellation = initializationCancellation.CancelAsync();
            async Task DrainAsync()
            {
                try { await initialization; }
                catch (Exception initializationError)
                {
                    if (!ReferenceEquals(initializationError, error) && initializationError is not OperationCanceledException)
                        initializationFailure = initializationError as TtLifecycleException ??
                            new TtLifecycleException(stage, initializationError);
                }
                try { await cancellation; }
                catch (Exception callbackError) { cancellationFailure = callbackError; }
                try { await pool.DisposeAsync(); }
                finally { initializationCancellation.Dispose(); }
            }
            var drain = DrainAsync();
            TtCleanup RetainCleanup(bool drainFailureReported = false)
            {
                var initializationFailureReported = false;
                var cancellationFailureReported = false;
                var cleanup = TtCleanupRegistry.Retain(pool.Snapshot.OwnerId, async () =>
                {
                    var failures = new List<Exception>();
                    try { await drain; }
                    catch (Exception drainError)
                    {
                        if (!drainFailureReported)
                        {
                            failures.Add(drainError);
                            drainFailureReported = true;
                        }
                        try { await pool.RetryCleanupAsync(); }
                        catch (Exception retryError)
                        {
                            failures.Add(retryError);
                        }
                    }
                    if (initializationFailure is not null && !initializationFailureReported)
                    {
                        initializationFailureReported = true;
                        failures.Add(initializationFailure);
                    }
                    if (cancellationFailure is not null && !cancellationFailureReported)
                    {
                        cancellationFailureReported = true;
                        failures.Add(new TtLifecycleException("StartupCancellation", cancellationFailure));
                    }
                    if (failures.Count == 1) throw failures[0];
                    if (failures.Count > 1) throw new AggregateException(failures);
                }, () => pool.OwnedResourceIds);
                return cleanup;
            }
            if (interrupted)
            {
                var interruptedStage = cancellationToken.IsCancellationRequested ? "StartupCanceled" : "StartupTimeout";
                throw new TtLifecycleException(interruptedStage, error, cancellationFailure, RetainCleanup());
            }
            var failure = error as TtLifecycleException ?? new TtLifecycleException(stage, error);
            try { await drain.WaitAsync(options.DrainTimeout); }
            catch (Exception cleanupError)
            {
                failure = new TtLifecycleException(failure.Stage, failure, cleanupError,
                    RetainCleanup(ReferenceEquals(drain.Exception?.InnerException, cleanupError)));
            }
            if (cancellationFailure is not null && failure.Cleanup is null)
                failure = failure.WithCleanupFailure(cancellationFailure);
            throw failure;
        }
        finally
        {
            if (!cleanupOwnsCancellation) initializationCancellation.Dispose();
        }
    }

    /// <summary>Closes static leases and resources. A later create starts a new owner.</summary>
    public static async Task DisposeAsync()
    {
        await OwnerGate.WaitAsync();
        try
        {
            var failures = new List<Exception>();
            foreach (var owner in Owners.Values)
            {
                try { await owner.DisposeAsync(); }
                catch (Exception error) { failures.Add(error); }
            }
            Owners.Clear();
            TimeSpan timeout;
            lock (ConfigurationGate) timeout = _options.DrainTimeout;
            try { await TtCleanupRegistry.DrainAsync(timeout); }
            catch (Exception error) { failures.Add(error); }
            lock (ConfigurationGate) _started = false;
            TtLifecycleException.ThrowIfAny("StaticDrain", failures);
        }
        finally { OwnerGate.Release(); }
    }
}

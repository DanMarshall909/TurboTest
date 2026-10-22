using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Runtime.CompilerServices;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class FactoryBackpressureTests
{
    private static CleanupLifetimeProbe? NextProbe;
    private static ConfigurationCancellationProbe? NextConfigurationProbe;

    [Fact]
    public async Task Cancellation_from_context_configuration_prevents_schema_initialization_and_discards_the_resource()
    {
        using var caller = new CancellationTokenSource();
        var probe = new ConfigurationCancellationProbe(caller);
        await using var pool = new BoundedPool();
        await using var factory = new TtFactory<CancelingConfigurationContext>(pool,
            new TtOptions { AutoMigrate = true }, ownsPool: true);
        NextConfigurationProbe = probe;
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.CreateLeaseAsync(
                cancellationToken: caller.Token).WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.True(caller.IsCancellationRequested);
            Assert.Equal(1, probe.Configurations);
            Assert.True(probe.SchemaInitializations == 0, "Schema initialization ran after configuration canceled acquisition.");
            Assert.Equal((1, 1, 0), (probe.Disposals, pool.Deleted, pool.Snapshot.Reserved));
            Assert.Empty(pool.OwnedResourceIds);
            await factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1), (probe.Disposals, pool.Deleted));
        }
        finally { NextConfigurationProbe = null; }
    }

    private sealed class ConfigurationCancellationProbe(CancellationTokenSource caller)
    {
        internal CancellationTokenSource Caller { get; } = caller;
        internal int Configurations;
        internal int SchemaInitializations;
        internal int Disposals;
    }

    public sealed class CancelingConfigurationContext : DbContext
    {
        private readonly ConfigurationCancellationProbe _probe;
        private DatabaseFacade? _database;

        public CancelingConfigurationContext(DbContextOptions<CancelingConfigurationContext> options) : base(options)
        {
            _probe = NextConfigurationProbe ?? throw new InvalidOperationException("Missing configuration cancellation probe.");
            NextConfigurationProbe = null;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            if (++_probe.Configurations > 1) throw new InvalidOperationException("Configuration was repeated.");
            _probe.Caller.Cancel();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<User>().HasKey(user => user.Id);
        }

        public override DatabaseFacade Database => _database ??= new ObservingDatabaseFacade(this, _probe);

        private sealed class ObservingDatabaseFacade(DbContext context, ConfigurationCancellationProbe probe) : DatabaseFacade(context)
        {
            public override Task<bool> EnsureCreatedAsync(CancellationToken cancellationToken = default)
            {
                probe.SchemaInitializations++;
                return base.EnsureCreatedAsync(cancellationToken);
            }
        }

        public override ValueTask DisposeAsync()
        {
            if (++_probe.Disposals > 1) throw new InvalidOperationException("Canceled context cleanup was repeated.");
            return base.DisposeAsync();
        }
    }

    [Fact]
    public async Task Repeated_successful_leases_release_completed_contexts_and_work_while_the_factory_remains_usable()
    {
        await using var pool = new BoundedPool();
        var factory = new TtFactory<TestDbContext>(pool, new TtOptions { AutoMigrate = false }, ownsPool: true);
        try
        {
            var completed = await Task.Run(() => CompleteLeases(factory));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            Assert.All(completed, entry =>
            {
                Assert.False(entry.Context.IsAlive);
                Assert.False(entry.Lease.IsAlive);
                Assert.False(entry.Acquisition.IsAlive);
            });
            Assert.Equal((1, 1, 0), (factory.PoolState.Reserved, factory.PoolState.Ready, factory.PoolState.Leased));
            await using var next = await factory.CreateLeaseAsync();
            Assert.NotNull(next.Context);
            GC.KeepAlive(factory);
        }
        finally { await factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
        Assert.Equal((0, 1), (factory.PoolState.Reserved, pool.Deleted));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Context, WeakReference Lease, WeakReference Acquisition)[] CompleteLeases(
        TtFactory<TestDbContext> factory)
    {
        var completed = new List<(WeakReference, WeakReference, WeakReference)>();
        for (var i = 0; i < 16; i++)
        {
            var acquiring = factory.CreateLeaseAsync();
            var lease = acquiring.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            try
            {
                completed.Add((new WeakReference(lease.Context), new WeakReference(lease), new WeakReference(acquiring)));
            }
            finally { lease.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        }
        return completed.ToArray();
    }

    [Fact]
    public async Task An_abandoned_acquisition_keeps_its_pending_context_cleanup_rooted_until_it_settles()
    {
        await using var pool = new BoundedPool();
        var factory = new TtFactory<HeldCleanupContext>(pool,
            new TtOptions { AutoMigrate = false }, ownsPool: true);
        var probe = new CleanupLifetimeProbe();
        try
        {
            await Task.Run(() => StartAbandonedAcquisition(factory, probe));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            Assert.Null(Volatile.Read(ref probe.Rescued));
            Assert.True(probe.Acquisition!.IsAlive);
            Assert.True(probe.Context!.IsAlive);
            Assert.Equal((1, 1, 0), (probe.Disposals, pool.Snapshot.Reserved, pool.Deleted));
            GC.KeepAlive(factory);
        }
        finally
        {
            Volatile.Write(ref probe.Abort, 1);
            var actual = probe.Acquisition?.Target as Task<TtLease<HeldCleanupContext>>;
            var context = Volatile.Read(ref probe.Rescued) ?? probe.Context?.Target as HeldCleanupContext;
            context?.Release.TrySetResult();
            try
            {
                if (actual is not null)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual.WaitAsync(TimeSpan.FromSeconds(2)));
            }
            finally { await factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
        }
        Assert.Equal((1, 0, 1), (probe.Disposals, pool.Snapshot.Reserved, pool.Deleted));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartAbandonedAcquisition(TtFactory<HeldCleanupContext> factory,
        CleanupLifetimeProbe probe)
    {
        using var caller = new CancellationTokenSource();
        var previous = SynchronizationContext.Current;
        var pump = new QueuedSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(pump);
        NextProbe = probe;
        try
        {
            var acquiring = factory.CreateLeaseAsync(_ => caller.Cancel(), caller.Token);
            probe.Acquisition = new WeakReference(acquiring, trackResurrection: true);
            // Pumping returns only after the callback that entered disposal has
            // returned to its await. This producer leaves the queue empty, so
            // neither queued work nor a worker stack roots cleanup during GC.
            pump.RunUntil(probe.Entered.Task);
            Assert.False(acquiring.IsCompleted);
        }
        finally
        {
            NextProbe = null;
            caller.Cancel();
            SynchronizationContext.SetSynchronizationContext(previous);
            pump.ReleaseToThreadPool();
        }
    }

    private sealed class CleanupLifetimeProbe
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal WeakReference? Context;
        internal WeakReference? Acquisition;
        internal HeldCleanupContext? Rescued;
        internal int Disposals;
        internal int Abort;
    }

    public sealed class HeldCleanupContext : DbContext
    {
        private readonly CleanupLifetimeProbe? _probe;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HeldCleanupContext(DbContextOptions<HeldCleanupContext> options) : base(options)
        {
            _probe = NextProbe ?? throw new InvalidOperationException("Missing cleanup lifetime probe.");
            NextProbe = null;
            _probe.Context = new WeakReference(this, trackResurrection: true);
        }

        // Rescue a prematurely collected context so the fault case can still release
        // and join actual cleanup. Correct factory ownership prevents this finalizer.
        ~HeldCleanupContext()
        {
            if (_probe is not null) Volatile.Write(ref _probe.Rescued, this);
        }

        public override async ValueTask DisposeAsync()
        {
            var probe = _probe!;
            if (Interlocked.Increment(ref probe.Disposals) > 1)
                throw new InvalidOperationException("Context cleanup was repeated.");
            probe.Entered.TrySetResult();
            if (Volatile.Read(ref probe.Abort) != 0) Release.TrySetResult();
            await Release.Task;
            await base.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_contexts_cannot_replace_their_fixture_owned_source(bool onConfiguring)
    {
        await using var pool = new BoundedPool();
        await using var fixture = new TtFactory<RedirectingContext>(pool, new TtOptions { AutoMigrate = false });
        RedirectingContext.Redirect = onConfiguring;
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.CreateLeaseAsync(
                onConfiguring ? null : builder => builder.UseNpgsql("Host=localhost;Database=unused;Username=unused")));
            Assert.Equal("ContextConfiguration", error.Stage);
            Assert.Null(error.InnerException);
            Assert.Equal(0, pool.Snapshot.Reserved);
            Assert.Equal(1, pool.Deleted);
        }
        finally { RedirectingContext.Redirect = false; }
    }

    [Fact]
    public async Task An_additional_context_is_validated_before_it_can_escape_its_lease()
    {
        await using var pool = new BoundedPool();
        await using var fixture = new TtFactory<RedirectingContext>(pool, new TtOptions { AutoMigrate = false });
        await using var lease = await fixture.CreateLeaseAsync();
        RedirectingContext.Redirect = true;
        try
        {
            var error = Assert.Throws<TtLifecycleException>(() => lease.CreateContext());
            Assert.Equal("ContextConfiguration", error.Stage);
            Assert.Equal(1, pool.Snapshot.Leased);
        }
        finally { RedirectingContext.Redirect = false; }
        await lease.DisposeAsync();
        Assert.Equal((1, 0), (pool.Snapshot.Ready, pool.Snapshot.Leased));
    }

    public sealed class RedirectingContext(DbContextOptions<RedirectingContext> options) : DbContext(options)
    {
        internal static bool Redirect { get; set; }
        protected override void OnConfiguring(DbContextOptionsBuilder builder)
        {
            if (Redirect) builder.UseNpgsql("Host=localhost;Database=unused;Username=unused");
        }
    }

    [Fact]
    public async Task A_throwing_shutdown_callback_reports_one_failure_after_draining_owned_resources()
    {
        await using var pool = new BoundedPool(throwOnCancellation: true);
        var factory = new TtFactory<TestDbContext>(pool, new TtOptions { AutoMigrate = false }, ownsPool: true);
        var lease = await factory.CreateLeaseAsync();
        var waiting = factory.CreateLeaseAsync();
        await pool.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failure = await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        try
        {
            var lifecycle = Assert.IsType<TtLifecycleException>(failure);
            Assert.Equal("FactoryDrain", lifecycle.Stage);
            Assert.DoesNotContain("private-cancellation-callback", lifecycle.ToString());
            Assert.Same(lifecycle, await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask()));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Context);
            Assert.Equal((1, 0), (pool.Deleted, factory.PoolState.Reserved));
            var cleanup = Assert.IsType<TtCleanup>(lifecycle.Cleanup);
            await cleanup.RetryCleanupAsync();
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Equal(1, pool.Deleted);
        }
        finally
        {
            await Record.ExceptionAsync(() => waiting);
            var final = await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask());
            if (final is TtLifecycleException { Cleanup: { } cleanup }) await cleanup.RetryCleanupAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_blocking_cancellation_callback_cannot_hide_the_drain_deadline_or_its_later_failure(bool lateFailure)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        await using var pool = new BoundedPool(lateFailure, () =>
        {
            if (Interlocked.Increment(ref callbacks) > 1) throw new InvalidOperationException("Cancellation was repeated.");
            entered.TrySetResult();
            release.Wait();
        });
        var factory = new TtFactory<TestDbContext>(pool,
            new TtOptions { AutoMigrate = false, DrainTimeout = TimeSpan.FromMilliseconds(100) }, ownsPool: true);
        var lease = await factory.CreateLeaseAsync();
        var waiting = factory.CreateLeaseAsync();
        await pool.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = Task.Run(() => factory.DisposeAsync().AsTask());
        TtCleanup? cleanup = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", failure.Stage);
            cleanup = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal((1, 0), (pool.Snapshot.Reserved, pool.Deleted));
            Assert.NotEmpty(cleanup.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.Set();
            if (lateFailure)
            {
                var callbackError = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal("FactoryDrain", callbackError.Stage);
                Assert.DoesNotContain("private-cancellation-callback", callbackError.ToString());
                await cleanup.RetryCleanupAsync();
            }
            else await pending.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Context);
            Assert.Equal((1, 1, 0), (callbacks, pool.Deleted, pool.Snapshot.Reserved));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Same(failure, await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask()));
        }
        finally
        {
            release.Set();
            await Record.ExceptionAsync(() => closing);
            var failure = await Record.ExceptionAsync(() => factory.DisposeAsync().AsTask());
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
            await Record.ExceptionAsync(() => waiting);
        }
    }

    [Fact]
    public async Task Closing_a_factory_cancels_capacity_waiters_before_draining_live_leases()
    {
        await using var pool = new BoundedPool();
        await using var factory = new TtFactory<TestDbContext>(pool,
            new TtOptions { AutoMigrate = false }, ownsPool: true);
        var leased = await factory.CreateLeaseAsync();
        var state = factory.PoolState;
        Assert.Equal((1, 1, 1, 0), (state.SlotLimit, state.Reserved, state.Leased, state.Ready));
        var waiting = factory.CreateLeaseAsync();
        await pool.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(waiting.IsCompleted);
        Assert.Equal((1, 1), (factory.PoolState.Reserved, factory.PoolState.QueuedAcquisitions));
        var closing = factory.DisposeAsync().AsTask();
        try
        {
            await closing.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            Assert.Throws<ObjectDisposedException>(() => _ = leased.Context);
            Assert.Equal(1, pool.Deleted);
            Assert.Equal(0, pool.Snapshot.Reserved);
            Assert.Equal(state.OwnerId, factory.PoolState.OwnerId);
            Assert.Equal((0, 0), (factory.PoolState.Reserved, factory.PoolState.QueuedAcquisitions));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => factory.CreateLeaseAsync());
        }
        finally
        {
            pool.AbortWaiting();
            await Record.ExceptionAsync(async () => await waiting);
            await closing;
        }
    }

    private sealed class BoundedPool : ITtDatabasePool
    {
        private readonly bool _throwOnCancellation;
        private readonly Action? _onCancellation;
        private readonly CancellationTokenSource _abort = new();
        private readonly TtSlotPool<NpgsqlDataSource> _slots;
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Deleted { get; private set; }
        public TtPoolSnapshot Snapshot => _slots.Snapshot;
        public TtResourceBudget Budget => throw new NotSupportedException("This in-process owner has no container budget.");
        public IReadOnlyList<string> OwnedResourceIds => Snapshot.Reserved == 0 ? Array.Empty<string>() : [Snapshot.OwnerId.ToString("N")];

        internal BoundedPool(bool throwOnCancellation = false, Action? onCancellation = null)
        {
            _throwOnCancellation = throwOnCancellation;
            _onCancellation = onCancellation;
            _slots = new TtSlotPool<NpgsqlDataSource>(1, 1,
            _ => NpgsqlDataSource.Create("Host=localhost;Database=unused;Username=unused"),
            (_, _) => Task.CompletedTask,
            async source => { await source.DisposeAsync(); Deleted++; });
        }

        public async Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
            using var callback = _throwOnCancellation || _onCancellation is not null
                ? linked.Token.Register(() =>
                {
                    _onCancellation?.Invoke();
                    if (_throwOnCancellation) throw new IOException("private-cancellation-callback");
                })
                : default;
            var acquisition = _slots.AcquireAsync(linked.Token);
            if (!acquisition.IsCompleted) Waiting.TrySetResult();
            var reservation = await acquisition;
            return new TtDatabase(reservation.Value,
                () => _slots.ReturnAsync(reservation, (_, _) => Task.CompletedTask),
                () => _slots.DiscardAsync(reservation));
        }

        internal void AbortWaiting() => _abort.Cancel();
        public ValueTask DisposeAsync() => _slots.DisposeAsync();
    }
}

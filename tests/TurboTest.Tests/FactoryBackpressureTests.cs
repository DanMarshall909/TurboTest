using Npgsql;
using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class FactoryBackpressureTests
{
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
        private readonly CancellationTokenSource _abort = new();
        private readonly TtSlotPool<NpgsqlDataSource> _slots;
        internal TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Deleted { get; private set; }
        public TtPoolSnapshot Snapshot => _slots.Snapshot;
        public TtResourceBudget Budget => throw new NotSupportedException("This in-process owner has no container budget.");

        internal BoundedPool() => _slots = new TtSlotPool<NpgsqlDataSource>(1, 1,
            _ => NpgsqlDataSource.Create("Host=localhost;Database=unused;Username=unused"),
            (_, _) => Task.CompletedTask,
            async source => { await source.DisposeAsync(); Deleted++; });

        public async Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
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

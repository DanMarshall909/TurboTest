using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class FactoryTimingTests
{
    [Fact]
    public async Task Actual_shutdown_includes_context_cleanup_before_pool_teardown()
    {
        var clock = new ManualPoolClock();
        var logger = new TimingLogger();
        var pool = new TimedPool();
        pool.AllowCheckout.SetResult(); pool.AllowReturn.SetResult();
        var factory = new TtFactory<HeldContext>(pool,
            new TtOptions { AutoMigrate = false, Logger = logger, LogLevel = LogLevel.Debug }, ownsPool: true, timeProvider: clock);
        var lease = await factory.CreateLeaseAsync();
        var context = lease.Context;
        var closing = factory.DisposeAsync().AsTask();
        try
        {
            await context.CleanupEntered.Task;
            Assert.False(pool.DrainEntered.Task.IsCompleted);
            clock.Advance(TimeSpan.FromSeconds(5));
            context.AllowCleanup.SetResult();
            await pool.DrainEntered.Task;
            clock.Advance(TimeSpan.FromSeconds(7)); pool.AllowDrain.SetResult();
            await closing;
            AssertDuration(logger, "Shutdown", 12000);
            AssertDuration(logger, "Return", 5000);
            Assert.Equal(1, context.Disposals);
        }
        finally
        {
            context.AllowCleanup.TrySetResult(); pool.AllowDrain.TrySetResult();
            await closing;
        }
    }

    [Fact]
    public async Task Canceled_checkout_keeps_its_observed_duration_and_does_not_create_a_return_cycle()
    {
        var clock = new ManualPoolClock();
        var logger = new TimingLogger(); var pool = new TimedPool();
        pool.AllowDrain.SetResult();
        await using var factory = new TtFactory<TestDbContext>(pool,
            new TtOptions { AutoMigrate = false, Logger = logger, LogLevel = LogLevel.Debug }, ownsPool: true, timeProvider: clock);
        using var cancel = new CancellationTokenSource();
        var checkout = factory.CreateLeaseAsync(cancellationToken: cancel.Token);
        await pool.CheckoutEntered.Task;
        clock.Advance(TimeSpan.FromSeconds(4)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkout);
        var record = Assert.Single(logger.Records);
        Assert.Equal(("Checkout", "Canceled", 4000d), (record["Operation"], record["Outcome"], record["DurationMilliseconds"]));
        Assert.Equal(0, pool.Returns);
    }

    [Fact]
    public async Task Public_timings_cover_checkout_shared_return_cycle_and_actual_shutdown()
    {
        var clock = new ManualPoolClock();
        var logger = new TimingLogger();
        var pool = new TimedPool();
        var factory = new TtFactory<TestDbContext>(pool,
            new TtOptions { AutoMigrate = false, Logger = logger, LogLevel = LogLevel.Debug }, ownsPool: true, timeProvider: clock);
        try
        {
            var checkout = factory.CreateLeaseAsync();
            await pool.CheckoutEntered.Task;
            clock.Advance(TimeSpan.FromSeconds(2));
            pool.AllowCheckout.SetResult();
            var lease = await checkout;
            AssertDuration(logger, "Checkout", 2000);
            clock.Advance(TimeSpan.FromSeconds(3));
            var returning = lease.DisposeAsync().AsTask();
            await pool.ReturnEntered.Task;
            var repeated = lease.DisposeAsync().AsTask();
            Assert.Same(returning, repeated);
            clock.Advance(TimeSpan.FromSeconds(5));
            pool.AllowReturn.SetResult();
            await returning;
            AssertDuration(logger, "Return", 5000);
            AssertDuration(logger, "Cycle", 10000);
            var closing = factory.DisposeAsync().AsTask();
            await pool.DrainEntered.Task;
            clock.Advance(TimeSpan.FromSeconds(7));
            pool.AllowDrain.SetResult();
            await closing;
            AssertDuration(logger, "Shutdown", 7000);
            await factory.DisposeAsync();
            Assert.Equal(4, logger.Records.Count);
            Assert.All(logger.Records, record =>
            {
                Assert.Equal("Succeeded", record["Outcome"]);
                Assert.Equal(pool.Snapshot.OwnerId, record["OwnerId"]);
                Assert.Equal(1, record["SlotLimit"]);
                Assert.DoesNotContain("private", string.Join(" ", record.Values));
            });
        }
        finally
        {
            pool.AllowCheckout.TrySetResult();
            pool.AllowReturn.TrySetResult();
            pool.AllowDrain.TrySetResult();
            await factory.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Throwing_diagnostic_observers_preserve_checkout_return_and_drain_results(bool failure)
    {
        var pool = new TimedPool(failure);
        pool.AllowCheckout.SetResult();
        pool.AllowReturn.SetResult();
        pool.AllowDrain.SetResult();
        var logger = new TimingLogger { Throw = true };
        var factory = new TtFactory<TestDbContext>(pool,
            new TtOptions { AutoMigrate = false, Logger = logger, LogLevel = LogLevel.Debug }, ownsPool: true);
        var lease = await factory.CreateLeaseAsync();
        var result = await Record.ExceptionAsync(() => lease.DisposeAsync().AsTask());
        if (failure) Assert.IsType<IOException>(result);
        else Assert.Null(result);
        await factory.DisposeAsync();
        Assert.Equal(1, pool.Returns);
        Assert.Equal(1, pool.Drains);
        Assert.Contains(logger.Records, record => (string?)record["Operation"] == "Return" &&
            (string?)record["Outcome"] == (failure ? "Failed" : "Succeeded"));
    }

    private static void AssertDuration(TimingLogger logger, string operation, double milliseconds)
    {
        var record = Assert.Single(logger.Records, record => (string?)record["Operation"] == operation);
        Assert.Equal(milliseconds, Assert.IsType<double>(record["DurationMilliseconds"]));
    }

    internal sealed class TimingLogger : ILogger
    {
        internal ConcurrentQueue<Dictionary<string, object?>> Records { get; } = new();
        internal bool Throw { get; init; }
        internal Action<Dictionary<string, object?>>? OnRecord { get; init; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id != 402) return;
            var record = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary();
            Records.Enqueue(record);
            OnRecord?.Invoke(record);
            if (Throw) throw new UnauthorizedAccessException("private-observer-payload");
        }
    }

    public sealed class HeldContext(DbContextOptions<HeldContext> options) : DbContext(options)
    {
        internal TaskCompletionSource CleanupEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource AllowCleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            if (++Disposals > 1) throw new InvalidOperationException("Repeated context cleanup.");
            CleanupEntered.SetResult(); await AllowCleanup.Task;
            await base.DisposeAsync();
        }
    }

    private sealed class TimedPool(bool failReturn = false) : ITtDatabasePool
    {
        private readonly NpgsqlDataSource _source = NpgsqlDataSource.Create("Host=localhost;Username=unused;Password=private;Database=unused");
        public TtPoolSnapshot Snapshot { get; } = new(Guid.NewGuid(), 1, 0, 1, 0, 0, 0, 0, 0, 0, 0);
        public TtResourceBudget Budget => throw new NotSupportedException();
        public IReadOnlyList<string> OwnedResourceIds => [];
        internal TaskCompletionSource CheckoutEntered { get; } = NewSignal();
        internal TaskCompletionSource AllowCheckout { get; } = NewSignal();
        internal TaskCompletionSource ReturnEntered { get; } = NewSignal();
        internal TaskCompletionSource AllowReturn { get; } = NewSignal();
        internal TaskCompletionSource DrainEntered { get; } = NewSignal();
        internal TaskCompletionSource AllowDrain { get; } = NewSignal();
        internal int Returns { get; private set; }
        internal int Drains { get; private set; }
        public async Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default)
        {
            CheckoutEntered.SetResult();
            await AllowCheckout.Task.WaitAsync(cancellationToken);
            return new TtDatabase(_source, async () =>
            {
                if (++Returns > 1) throw new InvalidOperationException("Repeated database return.");
                ReturnEntered.SetResult();
                await AllowReturn.Task;
                if (failReturn) throw new IOException("private-return-payload");
            }, () => Task.CompletedTask);
        }
        public async ValueTask DisposeAsync()
        {
            if (++Drains > 1) throw new InvalidOperationException("Repeated physical drain.");
            DrainEntered.SetResult();
            await AllowDrain.Task;
            await _source.DisposeAsync();
        }
        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

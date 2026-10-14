using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class ResetTimingTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Reset_measurements_distinguish_permit_wait_execution_and_validation_without_payloads()
    {
        var logger = new ResetLogger();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        var baseline = new TtBaselineDefinition<TestDbContext>("reset-timing-v1", TtSchemaInitialization.EnsureCreated,
            async (context, cancellation) =>
            {
                context.Users.Add(new User { Email = "timing-seed-secret@example.com" });
                await context.SaveChangesAsync(cancellation);
            });
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 2, Logger = logger, LogLevel = LogLevel.Debug }, baseline, default,
            afterSequenceRestore: async (_, _) =>
            {
                if (Interlocked.Increment(ref callbacks) != 1) return;
                entered.SetResult();
                await release.Task;
            });
        var first = await fixture.CreateLeaseAsync();
        var second = await fixture.CreateLeaseAsync();
        Task? firstReturn = null;
        Task? secondReturn = null;
        try
        {
            firstReturn = first.DisposeAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            secondReturn = second.DisposeAsync().AsTask();
            Assert.False(secondReturn.IsCompleted);
            using var queued = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (fixture.PoolState.WaitingResets == 0) await Task.Delay(1, queued.Token);
            Assert.Equal((2, 1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.WaitingResets,
                fixture.PoolState.ExecutingResets, fixture.PoolState.Ready));
            await Task.Delay(25);
            release.SetResult();
            await Task.WhenAll(firstReturn, secondReturn);
            var records = logger.Records.ToArray();
            Assert.Equal(2, records.Length);
            foreach (var record in records)
            {
                Assert.Equal("Rows", record["Strategy"]);
                Assert.Equal(true, record["Succeeded"]);
                Assert.IsType<Guid>(record["OwnerId"]);
                Assert.Equal(fixture.PoolState.OwnerId, record["OwnerId"]);
                var execution = Assert.IsType<double>(record["ResetExecutionMilliseconds"]);
                var validation = Assert.IsType<double>(record["ValidationMilliseconds"]);
                Assert.True(validation > 0);
                Assert.True(execution >= validation);
                Assert.DoesNotContain("timing-seed-secret", string.Join(" ", record.Values));
            }
            Assert.True(Assert.IsType<double>(records[1]["ResetPermitWaitMilliseconds"]) >
                Assert.IsType<double>(records[0]["ResetPermitWaitMilliseconds"]));
        }
        finally
        {
            release.TrySetResult();
            if (firstReturn is not null) await firstReturn;
            if (secondReturn is not null) await secondReturn;
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private sealed class ResetLogger : ILogger
    {
        internal ConcurrentQueue<Dictionary<string, object?>> Records { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 400 && eventId.Name == "BaselineReset")
                Records.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
        }
    }
}

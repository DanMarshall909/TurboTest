using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class ResetTimingTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Native_execution_measurement_stops_before_held_recovery_deletion()
    {
        var clock = new ManualPoolClock();
        var logger = new ResetLogger();
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resets = 0; var deletions = 0;
        var baseline = new TtBaselineDefinition<TestDbContext>("native-timing-boundary-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) => { context.Users.Add(new User { Email = "native-seed@example.test" }); await context.SaveChangesAsync(token); });
        await using var fixture = await Tt.CreatePreparedFactoryAsync(
            new TtOptions { PoolSize = 1, Logger = logger, LogLevel = LogLevel.Debug }, baseline, default,
            afterRowCommit: _ =>
            {
                if (Interlocked.Increment(ref resets) == 1)
                {
                    clock.Advance(TimeSpan.FromSeconds(2));
                    throw new IOException("private-held-recovery");
                }
                return Task.CompletedTask;
            }, dropDatabase: async (connection, name) =>
            {
                if (Interlocked.Increment(ref deletions) == 1) { deleting.SetResult(); await release.Task; }
                if (deletions > 2) throw new InvalidOperationException("Unexpected repeated physical deletion.");
                await using var command = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name.Replace("\"", "\"\"")}\" WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            }, timeProvider: clock);
        var lease = await fixture.CreateLeaseAsync();
        var returning = lease.DisposeAsync().AsTask();
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(5));
            release.SetResult();
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => returning);
            Assert.Contains("IOException", error.Message);
            var record = Assert.Single(logger.Records);
            Assert.Equal(2000d, record["ResetExecutionMilliseconds"]);
            Assert.Equal(0d, record["ResetPermitWaitMilliseconds"]);
            var recovery = Assert.Single(logger.Timings, item => Equals(item["Operation"], "RecoveryDeletion"));
            Assert.Equal(5000d, recovery["DurationMilliseconds"]);
            await using var next = await fixture.CreateLeaseAsync();
            Assert.Equal("native-seed@example.test", (await next.Context.Users.SingleAsync()).Email);
        }
        finally { release.TrySetResult(); await Record.ExceptionAsync(() => returning); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Reset_diagnostics_cannot_replace_success_or_material_failure(bool failReset)
    {
        var logger = new ResetLogger(throwOnce: true);
        var definition = new TtBaselineDefinition<TestDbContext>("safe-reset-timing-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) =>
            {
                context.Users.Add(new User { Email = "private-seed@example.test" });
                await context.SaveChangesAsync(token);
            });
        var callbacks = 0;
        await using var fixture = await Tt.CreatePreparedFactoryAsync(
            new TtOptions { PoolSize = 1, Logger = logger, LogLevel = LogLevel.Debug }, definition, default,
            afterRowCommit: _ =>
            {
                if (Interlocked.Increment(ref callbacks) == 1 && failReset)
                    throw new IOException("private-reset-payload");
                return Task.CompletedTask;
            });
        var lease = await fixture.CreateLeaseAsync();
        lease.Context.Users.Add(new User { Email = "private-load@example.test" });
        await lease.Context.SaveChangesAsync();
        var result = await Record.ExceptionAsync(() => lease.DisposeAsync().AsTask());
        if (failReset)
        {
            var error = Assert.IsType<TtLifecycleException>(result);
            Assert.Contains("IOException", error.Message);
            Assert.DoesNotContain("UnauthorizedAccessException", error.Message);
            Assert.DoesNotContain("private-reset", error.Message);
        }
        else Assert.Null(result);
        var record = Assert.Single(logger.Records);
        Assert.Equal(!failReset, record["Succeeded"]);
        Assert.DoesNotContain("private-", string.Join(" ", record.Values));
        await using var next = await fixture.CreateLeaseAsync();
        Assert.Equal("private-seed@example.test", (await next.Context.Users.SingleAsync()).Email);
    }

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
            var validationRecords = logger.Timings.Where(record => Equals(record["Operation"], "ResetValidation")).ToArray();
            Assert.Equal(2, validationRecords.Length);
            var preparationValidation = logger.Timings.Where(record => Equals(record["Operation"], "PreparationValidation")).ToArray();
            Assert.Equal(2, preparationValidation.Length);
            Assert.All(preparationValidation, record => Assert.True(Assert.IsType<double>(record["DurationMilliseconds"]) > 0));
            Assert.All(validationRecords, record =>
            {
                Assert.Equal("Succeeded", record["Outcome"]);
                Assert.True(Assert.IsType<double>(record["DurationMilliseconds"]) > 0);
            });
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

    private sealed class ResetLogger(bool throwOnce = false) : ILogger
    {
        private int _emissions;
        internal ConcurrentQueue<Dictionary<string, object?>> Records { get; } = new();
        internal ConcurrentQueue<Dictionary<string, object?>> Timings { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 402) Timings.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
            if (eventId.Id == 400 && eventId.Name == "BaselineReset")
            {
                Records.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
                if (throwOnce && Interlocked.Increment(ref _emissions) == 1)
                    throw new UnauthorizedAccessException("private-logging-payload");
            }
        }
    }
}

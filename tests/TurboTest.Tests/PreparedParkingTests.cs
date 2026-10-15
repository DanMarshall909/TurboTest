using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedParkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Rewarm_reuses_the_database_and_waits_for_full_seed_schema_and_workload_validation(bool throwingLogger)
    {
        var logger = new LifecycleLogger(throwingLogger);
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validations = 0;
        var definition = new TtBaselineDefinition<TestDbContext>("rewarm-seed-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) =>
            {
                context.Users.Add(new User { Email = "retained-seed@example.test" });
                await context.SaveChangesAsync(token);
            });
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 1, Logger = logger, LogLevel = LogLevel.Debug }, definition, default,
            beforeVerification: async _ =>
            {
                var count = Interlocked.Increment(ref validations);
                if (count > 2) throw new InvalidOperationException("The retained slot needed more than one reopening.");
                if (count == 2)
                {
                    begun.TrySetResult();
                    await release.Task;
                }
            });
        var original = await fixture.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(original.Context);
        await original.DisposeAsync();
        await fixture.MaintainIdleAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var waiting = fixture.CreateLeaseAsync(cancellationToken: cancel.Token);
        try
        {
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.Rewarming, fixture.PoolState.Ready));
            release.TrySetResult();
            await using var renewed = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
            var next = await PooledResetTests.ReadIdentityAsync(renewed.Context);
            Assert.Equal((identity.Database, identity.Oid), (next.Database, next.Oid));
            Assert.NotEqual(identity.Session, next.Session);
            var seed = await renewed.Context.Users.SingleAsync();
            Assert.Equal((1, "retained-seed@example.test"), (seed.Id, seed.Email));
            renewed.Context.Users.Add(new User { Email = "after-rewarm@example.test" });
            await renewed.Context.SaveChangesAsync();
            Assert.Equal(2, await renewed.Context.Users.MaxAsync(user => user.Id));
            Assert.Equal((1, 1, 0, 2), (fixture.PoolState.Reserved, fixture.PoolState.Leased,
                fixture.PoolState.Rewarming, validations));
            await renewed.DisposeAsync();
            await fixture.DisposeAsync();
            var rewarm = Assert.Single(logger.Timings, record => Equals(record["Operation"], "RewarmExecution"));
            var validation = Assert.Single(logger.Timings, record => Equals(record["Operation"], "RewarmValidation"));
            Assert.True(Assert.IsType<double>(validation["DurationMilliseconds"]) > 0);
            Assert.True(Assert.IsType<double>(rewarm["DurationMilliseconds"]) >= (double)validation["DurationMilliseconds"]!);
            Assert.Equal("Succeeded", validation["Outcome"]);
            var records = logger.Records.ToArray();
            Assert.Equal(new[] { "Delete", "Park", "Rewarm", "Teardown" },
                records.Select(record => Assert.IsType<string>(record["Operation"])).Order().ToArray());
            foreach (var record in records)
            {
                Assert.Equal(fixture.PoolState.OwnerId, record["OwnerId"]);
                Assert.Equal(true, record["Succeeded"]);
                Assert.True(Assert.IsType<double>(record["DurationMilliseconds"]) > 0);
                Assert.DoesNotContain("retained-seed", string.Join(" ", record.Values));
                Assert.DoesNotContain("Password=", string.Join(" ", record.Values));
            }
            var deletion = records.Single(record => Equals(record["Operation"], "Delete"));
            var teardown = records.Single(record => Equals(record["Operation"], "Teardown"));
            Assert.True((double)teardown["DurationMilliseconds"]! >= (double)deletion["DurationMilliseconds"]!);
        }
        finally
        {
            cancel.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(async () => { await using var lease = await waiting; });
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Parking_closes_owned_sessions_and_retains_the_database_oid_files_and_reservation()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var fixture = await Tt.CreateFactoryAsync(
            new TtBaselineDefinition<TestDbContext>("park-files-v1", TtSchemaInitialization.EnsureCreated,
                async (context, token) =>
                {
                    context.Users.Add(new User { Email = "parked-seed@example.test" });
                    await context.SaveChangesAsync(token);
                }), options => { options.PoolSize = 1; options.WarmFloor = 0; options.Password = password; });
        var lease = await fixture.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(lease.Context);
        var settings = new NpgsqlConnectionStringBuilder(lease.Context.Database.GetDbConnection().ConnectionString)
        {
            Database = "postgres", Pooling = false, Password = password
        };
        await using var observer = new NpgsqlConnection(settings.ConnectionString);
        await observer.OpenAsync();
        var oid = await ReadAsync<uint>("SELECT oid FROM pg_database WHERE datname = $1", identity.Database);
        await lease.DisposeAsync();
        Assert.True(await ReadAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = $1 AND usename = current_user", identity.Database) > 0);
        await fixture.MaintainIdleAsync();
        Assert.Equal(0L, await ReadAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE datname = $1 AND usename = current_user", identity.Database));
        Assert.Equal(oid, await ReadAsync<uint>("SELECT oid FROM pg_database WHERE datname = $1", identity.Database));
        Assert.True(await ReadAsync<long>("SELECT pg_database_size($1)", identity.Database) > 0);
        Assert.Equal((1, 0, 1), (fixture.PoolState.Reserved, fixture.PoolState.Ready, fixture.PoolState.Parked));
        await observer.DisposeAsync();
        await fixture.DisposeAsync();
        Assert.Equal(0, fixture.PoolState.Reserved);
        Assert.Empty(fixture.OwnedResourceIds);

        async Task<T> ReadAsync<T>(string sql, object value)
        {
            await using var command = new NpgsqlCommand(sql, observer);
            command.Parameters.Add(new NpgsqlParameter { Value = value });
            return (T)(await command.ExecuteScalarAsync())!;
        }
    }

    internal sealed class LifecycleLogger(bool throwing = false) : ILogger
    {
        private int _emissions;
        internal ConcurrentQueue<Dictionary<string, object?>> Records { get; } = new();
        internal ConcurrentQueue<Dictionary<string, object?>> Timings { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 402) Timings.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
            if (eventId.Id == 401 && eventId.Name == "PreparedLifecycle")
            {
                Records.Enqueue(((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary());
                if (throwing && Interlocked.Increment(ref _emissions) == 1)
                    throw new InvalidOperationException("private-logging-payload");
            }
        }
    }
}

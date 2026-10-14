using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedRecoveryTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Recovery_rejects_a_corrupt_clone_and_publishes_the_verified_seed_and_sequence()
    {
        var validCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clones = 0;
        var injectReset = 1;
        string? corruptDatabase = null;
        var definition = new TtBaselineDefinition<TestDbContext>("recover-seed-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) =>
            {
                context.Users.Add(new User { Email = "recovery-seed@example.test" });
                await context.SaveChangesAsync(token);
            });
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 1 }, definition, default,
            beforeVerification: async source =>
            {
                var candidate = Interlocked.Increment(ref clones);
                if (candidate > 3) throw new InvalidOperationException("Recovery prepared more than one verified retry.");
                if (candidate == 2)
                {
                    await using var connection = await source.OpenConnectionAsync();
                    corruptDatabase = connection.Database;
                    await using var command = new NpgsqlCommand("DELETE FROM \"Users\"", connection);
                    await command.ExecuteNonQueryAsync();
                }
                if (candidate == 3)
                {
                    validCandidate.TrySetResult();
                    await releaseCandidate.Task;
                }
            }, afterSequenceRestore: (_, _) => Interlocked.Exchange(ref injectReset, 0) == 1
                ? Task.FromException(new IOException("Reset failed after sequence restoration.")) : Task.CompletedTask);
        var dirty = await fixture.CreateLeaseAsync();
        var originalDatabase = dirty.Context.Database.GetDbConnection().Database;
        await dirty.Context.Users.ExecuteDeleteAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<TtLease<TestDbContext>>? waiting = null;
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => dirty.DisposeAsync().AsTask());
            Assert.Equal("BaselineRowReset", failure.Stage);
            waiting = fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
            await validCandidate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1, 0), (fixture.PoolState.Reserved, fixture.PoolState.Preparing, fixture.PoolState.Ready));
            releaseCandidate.TrySetResult();
            await using var clean = await waiting;
            Assert.NotEqual(originalDatabase, clean.Context.Database.GetDbConnection().Database);
            Assert.NotEqual(corruptDatabase, clean.Context.Database.GetDbConnection().Database);
            var seed = await clean.Context.Users.SingleAsync();
            Assert.Equal((1, "recovery-seed@example.test"), (seed.Id, seed.Email));
            clean.Context.Users.Add(new User { Email = "after-recovery@example.test" });
            await clean.Context.SaveChangesAsync();
            Assert.Equal(2, await clean.Context.Users.MaxAsync(user => user.Id));
            await clean.Context.Database.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = ANY($1)",
                (NpgsqlConnection)clean.Context.Database.GetDbConnection());
            command.Parameters.Add(new NpgsqlParameter { Value = new[] { originalDatabase, corruptDatabase! } });
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            Assert.Equal(3, clones);
        }
        finally
        {
            deadline.Cancel();
            releaseCandidate.TrySetResult();
            if (waiting is not null)
            {
                await Record.ExceptionAsync(async () => { await using var lease = await waiting; });
            }
            await Record.ExceptionAsync(() => dirty.DisposeAsync().AsTask());
        }
    }
}

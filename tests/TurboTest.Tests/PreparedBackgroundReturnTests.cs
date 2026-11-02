using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedBackgroundReturnTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(TtResetFailurePolicy.StopFixture)]
    [InlineData(TtResetFailurePolicy.RecoverAndReport)]
    [Trait("Category", "PostgreSQL")]
    public async Task The_captured_policy_controls_acquisition_and_both_policies_report_reset_failure_after_cleanup(TtResetFailurePolicy? policy)
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new TtOptions { PoolSize = 1 };
        if (policy is { } configured) options.ResetFailurePolicy = configured;
        var recover = policy == TtResetFailurePolicy.RecoverAndReport;
        var resets = 0; var deletions = 0;
        var baseline = new TtBaselineDefinition<TestDbContext>("background-failure-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) => { context.Users.Add(new User { Email = "fault-seed@example.test" }); await context.SaveChangesAsync(token); });
        var fixture = await Tt.CreatePreparedFactoryAsync(options, baseline, default,
            afterRowCommit: _ => Interlocked.Increment(ref resets) == 1
                ? Task.FromException(new IOException("private-reset-fault")) : Task.CompletedTask,
            dropDatabase: async (connection, name) =>
            {
                if (Interlocked.Increment(ref deletions) == 1) { deleting.SetResult(); await release.Task; }
                if (deletions > 2) throw new InvalidOperationException("Unexpected deletion retry.");
                await using var command = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name.Replace("\"", "\"\"")}\" WITH (FORCE)", connection);
                await command.ExecuteNonQueryAsync();
            });
        // A caller cannot change a running fixture's captured policy.
        options.ResetFailurePolicy = recover ? TtResetFailurePolicy.StopFixture : TtResetFailurePolicy.RecoverAndReport;
        var lease = await fixture.CreateLeaseAsync();
        var original = lease.Context.Database.GetDbConnection().Database;
        await lease.Context.Users.ExecuteDeleteAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var waiting = fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
        try
        {
            await lease.DisposeAsync();
            await deleting.Task.WaitAsync(deadline.Token);
            if (recover) Assert.False(waiting.IsCompleted);
            else
            {
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.Contains("IOException", failure.Message);
                Assert.DoesNotContain("private-", failure.ToString());
                var later = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.CreateLeaseAsync());
                Assert.Same(failure, later);
            }
            release.SetResult();
            if (recover)
            {
                await using var restored = await waiting;
                Assert.NotEqual(original, restored.Context.Database.GetDbConnection().Database);
                Assert.Equal("fault-seed@example.test", (await restored.Context.Users.SingleAsync()).Email);
            }
            var shutdown = await Assert.ThrowsAsync<TtLifecycleException>(() => fixture.DisposeAsync().AsTask());
            Assert.Contains("IOException", shutdown.Message);
            Assert.DoesNotContain("private-", shutdown.ToString());
            Assert.Empty(fixture.OwnedResourceIds);
            Assert.Equal(0, fixture.PoolState.Reserved);
            if (shutdown.Cleanup is { } cleanup) await cleanup.RetryCleanupAsync();
        }
        finally
        {
            release.TrySetResult();
            deadline.Cancel();
            await Record.ExceptionAsync(async () => { await using var extra = await waiting; });
            var error = await Record.ExceptionAsync(() => fixture.DisposeAsync().AsTask());
            if (error is TtLifecycleException { Cleanup: { } cleanup }) await cleanup.RetryCleanupAsync();
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_return_releases_the_test_while_the_pool_owns_reset_and_shutdown_waits_for_it()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resets = 0;
        var baseline = new TtBaselineDefinition<TestDbContext>("background-return-v1", TtSchemaInitialization.EnsureCreated,
            async (context, token) =>
            {
                context.Users.Add(new User { Email = "background-seed@example.test" });
                await context.SaveChangesAsync(token);
            });
        var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 2 }, baseline, default,
            afterRowCommit: async _ =>
            {
                if (Interlocked.Increment(ref resets) != 1) return;
                entered.TrySetResult();
                await release.Task;
            });
        var dirty = await fixture.CreateLeaseAsync();
        var context = dirty.Context;
        var secondary = dirty.CreateContext();
        await secondary.Database.OpenConnectionAsync();
        var dirtyName = dirty.Context.Database.GetDbConnection().Database;
        await dirty.Context.Users.ExecuteDeleteAsync();
        Task? shutdown = null;
        try
        {
            var returning = dirty.DisposeAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await returning.WaitAsync(TimeSpan.FromSeconds(1));
            await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => dirty.DisposeAsync().AsTask()));
            Assert.Throws<ObjectDisposedException>(() => dirty.Context.Users);
            Assert.Throws<ObjectDisposedException>(() => context.ChangeTracker);
            Assert.Throws<ObjectDisposedException>(() => secondary.ChangeTracker);

            await using var clean = await fixture.CreateLeaseAsync();
            Assert.NotEqual(dirtyName, clean.Context.Database.GetDbConnection().Database);
            Assert.Equal("background-seed@example.test", (await clean.Context.Users.SingleAsync()).Email);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var waiting = fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
            Assert.False(waiting.IsCompleted);
            Assert.Equal(1, fixture.PoolState.ExecutingResets);
            shutdown = fixture.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            Assert.False(shutdown.IsCompleted);
            release.TrySetResult();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(0, fixture.PoolState.Reserved);
            Assert.Equal(1, resets);
        }
        finally
        {
            release.TrySetResult();
            await dirty.DisposeAsync();
            await (shutdown ?? fixture.DisposeAsync().AsTask());
        }
    }
}

using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class ReadinessCancellationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cancellation_during_a_probe_preserves_its_connection_until_completion_and_never_reports_ready()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var database = await pool.GetDatabaseAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        NpgsqlConnection? probeConnection = null;
        var ready = TtPool.WaitForDatabaseReadyAsync(database.DataSource, async connection =>
        {
            probeConnection = connection;
            entered.TrySetResult();
            await release.Task;
            await using var query = new NpgsqlCommand("SELECT 1", connection);
            Assert.Equal(1, await query.ExecuteScalarAsync());
        }, cancellationToken: cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            Assert.False(ready.IsCompleted);
            Assert.Equal(System.Data.ConnectionState.Open, probeConnection!.State);
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ready.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(System.Data.ConnectionState.Closed, probeConnection.State);
        }
        finally { release.TrySetResult(); await Record.ExceptionAsync(() => ready); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cancellation_after_a_failed_probe_stops_retrying_and_releases_the_connection()
    {
        await using var pool = new TtPool(new TtOptions { PoolSize = 1 });
        await pool.InitializeAsync();
        await using var database = await pool.GetDatabaseAsync();
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        NpgsqlConnection? probeConnection = null;
        var failure = await Record.ExceptionAsync(() => TtPool.WaitForDatabaseReadyAsync(database.DataSource, connection =>
        {
            if (Interlocked.Increment(ref attempts) > 1)
                throw new InvalidOperationException("Canceled readiness retried its probe.");
            probeConnection = connection;
            cancellation.Cancel();
            throw new IOException("The probe failed while cancellation was requested.");
        }, maxAttempts: 2, cancellationToken: cancellation.Token));
        Assert.Equal(1, attempts);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        Assert.Equal(System.Data.ConnectionState.Closed, probeConnection!.State);
    }
}

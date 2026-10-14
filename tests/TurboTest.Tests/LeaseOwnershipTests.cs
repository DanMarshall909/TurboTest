using Microsoft.EntityFrameworkCore;
using Npgsql;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class LeaseOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reentrant_disposal_shares_the_pending_return_before_context_cleanup_starts(bool throughLease)
    {
        await using var source = NpgsqlDataSource.Create("Host=unused;Username=test");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbacks = 0;
        var resets = 0;
        var discards = 0;
        Task? nested = null;
        var database = new TtDatabase(source,
            () => { resets++; return Task.CompletedTask; },
            () => { discards++; return Task.CompletedTask; });
        var context = new RecordingContext(new(), "primary");
        var lease = new TtLease<RecordingContext>(context, () => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(), () => database.DisposeAsync().AsTask());
        database.OnDispose(async () =>
        {
            if (++callbacks > 1) throw new InvalidOperationException("Context cleanup was repeated.");
            nested = throughLease ? lease.DisposeAsync().AsTask() : database.DisposeAsync().AsTask();
            entered.TrySetResult();
            await release.Task;
            await context.DisposeAsync();
        });
        var attempt = throughLease ? lease.DisposeAsync().AsTask() : database.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(attempt, nested);
            Assert.False(attempt.IsCompleted);
            Assert.Equal((1, 0, 0), (callbacks, resets, discards));
        }
        finally { release.TrySetResult(); }
        await attempt.WaitAsync(TimeSpan.FromSeconds(2));
        if (throughLease) await lease.DisposeAsync();
        else await database.DisposeAsync();
        Assert.Equal((1, 1, 0), (callbacks, resets, discards));
    }

    [Fact]
    public async Task Failed_public_lease_return_is_shared_without_retrying_cleanup()
    {
        var attempts = 0;
        var error = new IOException("return failure");
        var context = new RecordingContext(new(), "primary");
        var lease = new TtLease<RecordingContext>(context,
            () => throw new InvalidOperationException("Late context created"),
            _ => throw new InvalidOperationException("Late context registered"), async () =>
            {
                attempts++;
                await context.DisposeAsync();
                throw error;
            });

        var first = await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        var repeated = await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask());
        Assert.Same(error, first);
        Assert.Same(error, repeated);
        Assert.Equal(1, attempts);
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Context);
        Assert.Throws<ObjectDisposedException>(() => lease.CreateContext());
    }

    [Fact]
    public async Task Public_lease_closes_all_contexts_and_waits_for_one_return()
    {
        await using var source = NpgsqlDataSource.Create("Host=unused;Username=test");
        var events = new List<string>();
        var permit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var database = new TtDatabase(source, async () =>
        {
            Assert.Equal(["close-primary", "close-secondary"], events);
            started.SetResult();
            await permit.Task;
            events.Add("ready");
        }, () => throw new InvalidOperationException("Unexpected discard"));
        void Register(RecordingContext context) => database.OnDispose(() => context.DisposeAsync().AsTask());
        var primary = new RecordingContext(events, "primary");
        Register(primary);
        var lease = new TtLease<RecordingContext>(primary, () => new(events, "secondary"),
            Register, () => database.DisposeAsync().AsTask());
        Assert.Same(primary, lease.Context);
        var secondary = lease.CreateContext();
        var first = lease.DisposeAsync().AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var concurrent = lease.DisposeAsync().AsTask();
        try
        {
            Assert.False(concurrent.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => _ = lease.Context);
            Assert.Throws<ObjectDisposedException>(() => lease.CreateContext());
        }
        finally { permit.TrySetResult(); }
        await Task.WhenAll(first, concurrent).WaitAsync(TimeSpan.FromSeconds(2));
        await lease.DisposeAsync();
        Assert.Equal(["close-primary", "close-secondary", "ready"], events);
        Assert.Throws<ObjectDisposedException>(() => _ = secondary.ChangeTracker);
    }

    public sealed class RecordingContext(List<string> events, string name) : DbContext
    {
        public override ValueTask DisposeAsync()
        {
            events.Add($"close-{name}");
            return base.DisposeAsync();
        }
    }
}

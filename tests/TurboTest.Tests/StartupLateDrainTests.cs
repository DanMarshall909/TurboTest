using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StartupLateDrainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_cleanup_replays_only_drain_failures_not_already_delivered(bool drainDeadline)
    {
        var pool = new StartupPool { FailInitialization = true };
        var startup = Tt.InitializeOwnedPoolAsync(pool,
            new TtOptions { DrainTimeout = TimeSpan.FromMilliseconds(50) }, pool.InitializeAsync,
            "TestInitialize", default);
        TtCleanup? cleanup = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            pool.ReleaseInitialization.TrySetResult();
            await pool.Deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (!drainDeadline) pool.ReleaseDeletion.TrySetResult();
            var initial = await Assert.ThrowsAsync<TtLifecycleException>(() => startup);
            Assert.Contains("InvalidOperationException", initial.Message);
            Assert.Equal(!drainDeadline, initial.Message.Contains("IOException"));
            cleanup = Assert.IsType<TtCleanup>(initial.Cleanup);
            pool.ReleaseDeletion.TrySetResult();
            if (drainDeadline)
            {
                var late = await Assert.ThrowsAsync<TtLifecycleException>(() => cleanup.RetryCleanupAsync());
                Assert.Contains("IOException", late.Message);
            }
            await cleanup.RetryCleanupAsync();
            Assert.Equal((0, 2), (pool.Snapshot.Reserved, pool.DeleteAttempts));
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            pool.ReleaseDeletion.TrySetResult();
            await Record.ExceptionAsync(() => startup);
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
            await pool.RetryCleanupAsync();
        }
    }

    [Fact]
    public async Task Canceled_startup_retains_an_unseen_late_drain_failure_after_physical_retry_succeeds()
    {
        var pool = new StartupPool();
        using var cancellation = new CancellationTokenSource();
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions(), pool.InitializeAsync,
            "TestInitialize", cancellation.Token);
        TtCleanup? cleanup = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            var interrupted = await Assert.ThrowsAsync<TtLifecycleException>(() => startup);
            Assert.Equal("StartupCanceled", interrupted.Stage);
            Assert.DoesNotContain("IOException", interrupted.Message);
            cleanup = Assert.IsType<TtCleanup>(interrupted.Cleanup);
            var retry = cleanup.RetryCleanupAsync();
            pool.ReleaseInitialization.TrySetResult();
            await pool.Deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(retry.IsCompleted);
            Assert.Single(cleanup.OwnedResourceIds);
            pool.ReleaseDeletion.TrySetResult();
            var late = await Assert.ThrowsAsync<TtLifecycleException>(() => retry.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("IOException", late.Message);
            Assert.DoesNotContain("private-drain-detail", late.ToString());
            Assert.Null(late.InnerException);
            Assert.Same(cleanup, late.Cleanup);
            Assert.Equal((0, 2), (pool.Snapshot.Reserved, pool.DeleteAttempts));
            await cleanup.RetryCleanupAsync();
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Equal(2, pool.DeleteAttempts);
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            pool.ReleaseDeletion.TrySetResult();
            await Record.ExceptionAsync(() => startup);
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
            await pool.RetryCleanupAsync();
        }
    }

    private sealed class StartupPool : ITtDatabasePool
    {
        private readonly TtSlotPool<object> _slots;
        internal TaskCompletionSource Initialized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseInitialization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Deleting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseDeletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int DeleteAttempts { get; private set; }
        internal bool FailInitialization { get; init; }
        public TtPoolSnapshot Snapshot => _slots.Snapshot;
        public TtResourceBudget Budget => TtResourceBudget.Create(new TtOptions { PoolSize = 1 }, true);
        public IReadOnlyList<string> OwnedResourceIds => Snapshot.Reserved == 0 ? [] : [Snapshot.OwnerId.ToString("N")];

        internal StartupPool()
        {
            _slots = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
                async _ =>
                {
                    var attempt = ++DeleteAttempts;
                    if (attempt == 2) return;
                    if (attempt > 2) throw new InvalidOperationException("Deletion exceeded its finite oracle.");
                    Deleting.TrySetResult();
                    await ReleaseDeletion.Task;
                    throw new IOException("private-drain-detail");
                });
        }

        internal async Task InitializeAsync(CancellationToken _)
        {
            await _slots.WarmAsync(1, default);
            Initialized.TrySetResult();
            await ReleaseInitialization.Task;
            if (FailInitialization) throw new InvalidOperationException("private-initialization-detail");
        }

        public Task<TtDatabase> GetDatabaseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => _slots.DisposeAsync();
        public Task RetryCleanupAsync() => _slots.RetryCleanupAsync();
    }
}

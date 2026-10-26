using System.Collections.Concurrent;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class StartupLateDrainTests
{
    [Fact]
    public async Task Startup_recovery_preserves_four_distinct_safe_failures_and_every_remaining_child_owner()
    {
        var children = new ConcurrentDictionary<string, byte>();
        var deletions = new ConcurrentDictionary<string, int>();
        var names = new[] { "seed-child", "callback-child", "drain-child", "retry-child" };
        var handles = names.ToDictionary(name => name, name => RetainChild(name, children, deletions));
        TtLifecycleException Failure(string stage, string state, string child) => new(stage,
            new Npgsql.PostgresException("private-" + stage, "ERROR", "ERROR", state), cleanup: handles[child]);
        var seedFailure = Failure("SeedStage", "42P01", "seed-child");
        var callbackFailure = Failure("CallbackStage", "42501", "callback-child");
        var drainFailure = Failure("HostDrain", "53100", "drain-child");
        var retryFailure = Failure("HostRetry", "53200", "retry-child");
        var pool = new StartupPool
        {
            FailedDeleteAttempts = 2,
            DeleteFailure = attempt => attempt == 1 ? drainFailure : retryFailure
        };
        using var caller = new CancellationTokenSource();
        CancellationTokenRegistration registration = default;
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions(), async token =>
        {
            registration = token.Register(() => throw callbackFailure);
            await pool.InitializeAsync(token);
            throw seedFailure;
        }, "BaselineInitialize", caller.Token);
        TtCleanup? recovery = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            caller.Cancel();
            var interrupted = await Assert.ThrowsAsync<TtLifecycleException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("StartupCanceled", interrupted.Stage);
            recovery = Assert.IsType<TtCleanup>(interrupted.Cleanup);
            var pending = recovery.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            Assert.Equal(0, pool.DeleteAttempts);
            pool.ReleaseInitialization.TrySetResult();
            await pool.Deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(pending.IsCompleted);
            pool.ReleaseDeletion.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            recovery = Assert.IsType<TtCleanup>(failure.Cleanup);
            foreach (var expected in new[] { ("SeedStage", "42P01"), ("CallbackStage", "42501"), ("HostDrain", "53100"), ("HostRetry", "53200") })
            {
                Assert.Contains(expected.Item1, failure.Message);
                Assert.Contains(expected.Item2, failure.Message);
            }
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.Equal(names.Append(pool.Snapshot.OwnerId.ToString("N")).Order().ToArray(), recovery.OwnedResourceIds.Order().ToArray());
            Assert.Equal((1, 2), (pool.Snapshot.Reserved, pool.DeleteAttempts));
            Assert.All(names, name => Assert.Equal(0, deletions.GetValueOrDefault(name)));
            await recovery.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await recovery.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(recovery.OwnedResourceIds);
            Assert.Empty(children);
            Assert.All(names, name => Assert.Equal(1, deletions[name]));
            Assert.Equal((0, 3), (pool.Snapshot.Reserved, pool.DeleteAttempts));
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            pool.ReleaseDeletion.TrySetResult();
            var failure = await Record.ExceptionAsync(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            recovery ??= (failure as TtLifecycleException)?.Cleanup;
            foreach (var cleanup in handles.Values.Append(recovery).OfType<TtCleanup>())
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            registration.Dispose();
            await pool.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Initializer_child_cleanup_does_not_hide_a_distinct_callback_failure_or_its_owner(
        bool callbackFails, bool outerDrainFails)
    {
        var children = new ConcurrentDictionary<string, byte>();
        var deletions = new ConcurrentDictionary<string, int>();
        var seedChild = RetainChild("seed-child", children, deletions);
        var callbackChild = callbackFails ? RetainChild("callback-child", children, deletions) : null;
        var seedFailure = new TtLifecycleException("SeedStage",
            new Npgsql.PostgresException("private-seed", "ERROR", "ERROR", "42P01"), cleanup: seedChild);
        var callbackFailure = callbackFails ? new TtLifecycleException("CallbackStage",
            new Npgsql.PostgresException("private-callback", "ERROR", "ERROR", "42501"), cleanup: callbackChild) : null;
        var pool = new StartupPool { FailedDeleteAttempts = outerDrainFails ? 1 : 0 };
        CancellationTokenRegistration registration = default;
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions(), async token =>
        {
            if (callbackFailure is not null) registration = token.Register(() => throw callbackFailure);
            await pool.InitializeAsync(token);
            throw seedFailure;
        }, "BaselineInitialize", default);
        TtCleanup? recovery = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            pool.ReleaseDeletion.TrySetResult();
            pool.ReleaseInitialization.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            recovery = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Equal(("SeedStage", "42P01"), (failure.Stage, failure.SqlState));
            Assert.Contains("seed-child", recovery.OwnedResourceIds);
            if (callbackFails && !outerDrainFails)
            {
                Assert.Contains("CallbackStage", failure.Message);
                Assert.Contains("42501", failure.Message);
                Assert.Equal(new[] { "callback-child", "seed-child" }, recovery.OwnedResourceIds.Order().ToArray());
            }
            else Assert.DoesNotContain("CallbackStage", failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.Equal((1, outerDrainFails ? 1 : 0), (pool.DeleteAttempts, pool.Snapshot.Reserved));
            Assert.Equal(0, deletions.GetValueOrDefault("seed-child"));
            Assert.Equal(0, deletions.GetValueOrDefault("callback-child"));
            if (outerDrainFails)
            {
                var callback = await Assert.ThrowsAsync<TtLifecycleException>(() => recovery.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains("CallbackStage", callback.Message);
                Assert.Contains("42501", callback.Message);
                Assert.DoesNotContain("SeedStage", callback.Message);
                recovery = Assert.IsType<TtCleanup>(callback.Cleanup);
                Assert.Equal(new[] { "callback-child" }, recovery.OwnedResourceIds);
            }
            await recovery.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await recovery.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(recovery.OwnedResourceIds);
            Assert.Empty(children);
            Assert.Equal(1, deletions["seed-child"]);
            Assert.Equal(callbackFails ? 1 : 0, deletions.GetValueOrDefault("callback-child"));
            Assert.Equal((outerDrainFails ? 2 : 1, 0), (pool.DeleteAttempts, pool.Snapshot.Reserved));
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            pool.ReleaseDeletion.TrySetResult();
            var failure = await Record.ExceptionAsync(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            recovery ??= (failure as TtLifecycleException)?.Cleanup;
            foreach (var cleanup in new[] { recovery, seedChild, callbackChild }.OfType<TtCleanup>())
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            registration.Dispose();
            await pool.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task An_initializer_owned_timeout_preserves_its_stage_and_completes_physical_cleanup()
    {
        var pool = new StartupPool { FailedDeleteAttempts = 0 };
        var startup = Tt.InitializeOwnedPoolAsync(pool,
            new TtOptions { StartupTimeout = TimeSpan.FromSeconds(10) }, async token =>
            {
                await pool.InitializeAsync(token);
                throw new TimeoutException("private-initializer-timeout");
            }, "BaselineInitialize", default);
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(startup.IsCompleted);
            pool.ReleaseInitialization.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("BaselineInitialize", failure.Stage);
            Assert.Contains(nameof(TimeoutException), failure.Message);
            Assert.Null(failure.SqlState);
            Assert.Null(failure.InnerException);
            Assert.Null(failure.Cleanup);
            Assert.DoesNotContain("private-initializer-timeout", failure.ToString());
            Assert.Equal((1, 0), (pool.DeleteAttempts, pool.Snapshot.Reserved));
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            var failure = await Record.ExceptionAsync(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            if (failure is TtLifecycleException { Cleanup: { } cleanup })
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            await pool.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(true, "Success")]
    [InlineData(false, "Success")]
    [InlineData(true, "OwnedCancellation")]
    [InlineData(false, "OwnedCancellation")]
    [InlineData(true, "Material")]
    [InlineData(false, "Material")]
    [InlineData(true, "SafeMaterial")]
    [InlineData(false, "SafeMaterial")]
    [InlineData(true, "Timeout")]
    [InlineData(false, "Timeout")]
    public async Task Interrupted_startup_reports_the_actual_late_initializer_failure_once_after_physical_cleanup(
        bool callerCancellation, string completion)
    {
        var pool = new StartupPool { FailedDeleteAttempts = 0 };
        using var caller = new CancellationTokenSource();
        Exception? material = completion switch
        {
            "Material" => new IOException("private-late-initializer"),
            "SafeMaterial" => new TtLifecycleException("LateSeedWrite",
                new Npgsql.PostgresException("private-late-seed", "ERROR", "ERROR", "53100")),
            "Timeout" => new TimeoutException("private-owned-timeout"),
            _ => null
        };
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions
        {
            StartupTimeout = callerCancellation ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(50)
        }, async token =>
        {
            await pool.InitializeAsync(token);
            if (completion == "OwnedCancellation") token.ThrowIfCancellationRequested();
            if (material is not null) throw material;
        }, "BaselineInitialize", caller.Token);
        TtCleanup? cleanup = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (callerCancellation) caller.Cancel();
            var interrupted = await Assert.ThrowsAsync<TtLifecycleException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(callerCancellation ? "StartupCanceled" : "StartupTimeout", interrupted.Stage);
            cleanup = Assert.IsType<TtCleanup>(interrupted.Cleanup);
            Assert.Single(cleanup.OwnedResourceIds);
            var recovery = cleanup.RetryCleanupAsync();
            Assert.False(recovery.IsCompleted);
            Assert.Equal(0, pool.DeleteAttempts);
            pool.ReleaseInitialization.TrySetResult();

            if (material is not null)
            {
                var late = await Assert.ThrowsAsync<TtLifecycleException>(() => recovery.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal((material as TtLifecycleException)?.Stage ?? "BaselineInitialize", late.Stage);
                Assert.Equal((material as TtLifecycleException)?.SqlState, late.SqlState);
                if (material is not TtLifecycleException) Assert.Contains(material.GetType().Name, late.Message);
                Assert.DoesNotContain("private-", late.ToString());
                Assert.Null(late.InnerException);
                Assert.Same(cleanup, late.Cleanup);
            }
            else await recovery.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 0), (pool.DeleteAttempts, pool.Snapshot.Reserved));
            Assert.Empty(cleanup.OwnedResourceIds);
            await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, pool.DeleteAttempts);
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            var failure = await Record.ExceptionAsync(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2)));
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            await pool.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task Successful_initialization_releases_its_cancellation_handle_when_actual_work_finishes()
    {
        var pool = new StartupPool { FailedDeleteAttempts = 0 };
        using var caller = new CancellationTokenSource();
        CancellationToken owned = default;
        WaitHandle? handle = null;
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions(), token =>
        {
            owned = token;
            handle = token.WaitHandle;
            return pool.InitializeAsync(token);
        }, "TestInitialize", caller.Token);
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(handle, owned.WaitHandle);
            Assert.False(handle!.SafeWaitHandle.IsClosed);
            Assert.False(startup.IsCompleted);
            pool.ReleaseInitialization.TrySetResult();
            await startup.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(handle.SafeWaitHandle.IsClosed);
            Assert.Throws<ObjectDisposedException>(() => owned.WaitHandle);
            Assert.False(caller.IsCancellationRequested);
            Assert.False(caller.Token.WaitHandle.SafeWaitHandle.IsClosed);
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            await Record.ExceptionAsync(() => startup);
            await pool.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Interrupted_initialization_keeps_cancellation_alive_for_owned_work_and_releases_it_after_drain(
        bool callerCancellation, bool failedDeletion)
    {
        var pool = new StartupPool { GateDeletion = true, FailedDeleteAttempts = failedDeletion ? 1 : 0 };
        using var caller = new CancellationTokenSource();
        using var releaseCallback = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializationFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken owned = default;
        CancellationTokenRegistration registration = default;
        WaitHandle? handle = null;
        var callbackCalls = 0;
        var startup = Tt.InitializeOwnedPoolAsync(pool, new TtOptions
        {
            StartupTimeout = callerCancellation ? TimeSpan.FromSeconds(10) : TimeSpan.FromMilliseconds(100)
        }, async token =>
        {
            owned = token;
            handle = token.WaitHandle;
            registration = token.Register(() =>
            {
                Interlocked.Increment(ref callbackCalls);
                callbackEntered.TrySetResult();
                releaseCallback.Wait();
            });
            await pool.InitializeAsync(token);
            initializationFinished.TrySetResult();
        }, "TestInitialize", caller.Token);
        TtCleanup? cleanup = null;
        try
        {
            await pool.Initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (callerCancellation) caller.Cancel();
            var interrupted = await Assert.ThrowsAsync<TtLifecycleException>(() => startup.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(callerCancellation ? "StartupCanceled" : "StartupTimeout", interrupted.Stage);
            cleanup = Assert.IsType<TtCleanup>(interrupted.Cleanup);
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AssertSourceAlive();
            var pending = cleanup.RetryCleanupAsync();
            pool.ReleaseInitialization.TrySetResult();
            await initializationFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            AssertSourceAlive();
            Assert.False(pending.IsCompleted);
            Assert.False(pool.Deleting.Task.IsCompleted);
            releaseCallback.Set();
            await pool.Deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(pending.IsCompleted);
            Assert.Single(cleanup.OwnedResourceIds);
            pool.ReleaseDeletion.TrySetResult();
            if (failedDeletion)
            {
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Contains(nameof(IOException), failure.Message);
                Assert.Same(cleanup, failure.Cleanup);
                await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            else await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(handle!.SafeWaitHandle.IsClosed);
            Assert.Throws<ObjectDisposedException>(() => owned.WaitHandle);
            Assert.Equal((1, failedDeletion ? 2 : 1, 0), (callbackCalls, pool.DeleteAttempts, pool.Snapshot.Reserved));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.False(caller.Token.WaitHandle.SafeWaitHandle.IsClosed);
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            releaseCallback.Set();
            pool.ReleaseDeletion.TrySetResult();
            var failure = await Record.ExceptionAsync(() => startup);
            cleanup ??= (failure as TtLifecycleException)?.Cleanup;
            if (cleanup is not null)
            {
                await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
                await cleanup.RetryCleanupAsync();
            }
            registration.Dispose();
            await pool.RetryCleanupAsync();
        }

        void AssertSourceAlive()
        {
            Assert.Same(handle, owned.WaitHandle);
            Assert.False(handle!.SafeWaitHandle.IsClosed);
        }
    }

    [Fact]
    public async Task Startup_recovery_reports_cancellation_drain_and_retry_failures_together_before_successful_recovery()
    {
        var pool = new StartupPool { FailedDeleteAttempts = 2, ThrowCancellationCallback = true };
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
            cleanup = Assert.IsType<TtCleanup>(interrupted.Cleanup);
            var retry = cleanup.RetryCleanupAsync();
            pool.ReleaseInitialization.TrySetResult();
            await pool.Deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            pool.ReleaseDeletion.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => retry.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains(nameof(IOException), failure.Message);
            Assert.Contains("RetryHostDelete", failure.Message);
            Assert.Contains("53100", failure.Message);
            Assert.Contains("StartupCancellation", failure.Message);
            Assert.Contains(nameof(NotSupportedException), failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.Same(cleanup, failure.Cleanup);
            Assert.Single(cleanup.OwnedResourceIds);
            Assert.Equal(2, pool.DeleteAttempts);
            await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(cleanup.OwnedResourceIds);
            Assert.Equal((0, 3), (pool.Snapshot.Reserved, pool.DeleteAttempts));
            await cleanup.RetryCleanupAsync();
            Assert.Equal(3, pool.DeleteAttempts);
        }
        finally
        {
            pool.ReleaseInitialization.TrySetResult();
            pool.ReleaseDeletion.TrySetResult();
            await Record.ExceptionAsync(() => startup);
            if (cleanup is not null)
                for (var attempt = 0; attempt < 3; attempt++)
                    await Record.ExceptionAsync(() => cleanup.RetryCleanupAsync());
            await pool.RetryCleanupAsync();
        }
    }

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

    private static TtCleanup RetainChild(string name, ConcurrentDictionary<string, byte> children,
        ConcurrentDictionary<string, int> deletions)
    {
        Assert.True(children.TryAdd(name, 0));
        return TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            if (deletions.AddOrUpdate(name, 1, (_, previous) => previous + 1) > 1)
                throw new InvalidOperationException("Confirmed child cleanup was repeated.");
            Assert.True(children.TryRemove(name, out _));
            return Task.CompletedTask;
        }, () => children.ContainsKey(name) ? [name] : []);
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
        internal int FailedDeleteAttempts { get; init; } = 1;
        internal bool ThrowCancellationCallback { get; init; }
        internal bool GateDeletion { get; init; }
        internal Func<int, Exception>? DeleteFailure { get; init; }
        public TtPoolSnapshot Snapshot => _slots.Snapshot;
        public TtResourceBudget Budget => TtResourceBudget.Create(new TtOptions { PoolSize = 1 }, true);
        public IReadOnlyList<string> OwnedResourceIds => Snapshot.Reserved == 0 ? [] : [Snapshot.OwnerId.ToString("N")];

        internal StartupPool()
        {
            _slots = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask,
                async _ =>
                {
                    var attempt = ++DeleteAttempts;
                    if (attempt > FailedDeleteAttempts + 1) throw new InvalidOperationException("Deletion exceeded its finite oracle.");
                    if (attempt == 1 && (GateDeletion || FailedDeleteAttempts > 0))
                    {
                        Deleting.TrySetResult();
                        await ReleaseDeletion.Task;
                    }
                    if (attempt > FailedDeleteAttempts) return;
                    if (DeleteFailure is not null) throw DeleteFailure(attempt);
                    if (attempt > 1)
                        throw new TtLifecycleException("RetryHostDelete",
                            new Npgsql.PostgresException("private-retry-detail", "ERROR", "ERROR", "53100"));
                    throw new IOException("private-drain-detail");
                });
        }

        internal async Task InitializeAsync(CancellationToken token)
        {
            using var registration = ThrowCancellationCallback
                ? token.Register(() => throw new NotSupportedException("private-cancel-detail")) : default;
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

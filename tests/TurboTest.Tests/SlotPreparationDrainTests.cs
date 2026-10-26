using System.Collections.Concurrent;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotPreparationDrainTests
{
    [Fact]
    public async Task A_terminal_failure_observed_after_abandoned_preparation_is_not_replayed_at_drain()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new TtLifecycleException("AbandonedSeed",
            new Npgsql.PostgresException("private-abandoned-seed", "ERROR", "ERROR", "53100"));
        var creates = 0;
        var deletes = 0;
        var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            if (++creates > 1) throw new InvalidOperationException("Terminal preparation created another resource.");
            return new Resource(id);
        }, async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            throw expected;
        }, _ =>
        {
            if (++deletes > 1) throw new InvalidOperationException("Confirmed preparation deletion was repeated.");
            return Task.CompletedTask;
        });
        using var caller = new CancellationTokenSource();
        var abandoned = owner.AcquireAsync(caller.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned.WaitAsync(TimeSpan.FromSeconds(2)));
            release.TrySetResult();
            using var settled = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (owner.Snapshot.Reserved != 0) await Task.Delay(1, settled.Token);
            Assert.Equal((1, 1, 0), (creates, deletes, owner.Snapshot.QueuedAcquisitions));
            var delivered = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.AcquireAsync());
            Assert.Same(expected, delivered);
            Assert.Equal(("AbandonedSeed", "53100"), (delivered.Stage, delivered.SqlState));
            Assert.DoesNotContain("private-", delivered.ToString());
            Assert.Null(delivered.InnerException);
            var drain = await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Null(drain);
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1, 0), (creates, deletes, owner.Snapshot.Reserved));
        }
        finally
        {
            release.TrySetResult();
            caller.Cancel();
            await Record.ExceptionAsync(() => abandoned.WaitAsync(TimeSpan.FromSeconds(2)));
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task A_retry_queued_before_shutdown_cannot_create_another_resource_after_closure()
    {
        var clock = new HeldRetryClock();
        var created = new ConcurrentQueue<Guid>();
        var deleted = new ConcurrentQueue<Guid>();
        var reservations = 0;
        var preparations = 0;
        var owner = new TtSlotPool<Resource>(1, 1, _ =>
        {
            if (Interlocked.Increment(ref reservations) > 2)
                throw new InvalidOperationException("Closed preparation invoked the resource factory again.");
            var resource = new Resource(Guid.NewGuid());
            created.Enqueue(resource.Id);
            return resource;
        }, (_, _) => Interlocked.Increment(ref preparations) == 1 ? Task.CompletedTask
            : Task.FromException(new IOException("private-replacement-preparation")), resource =>
        {
            if (deleted.Contains(resource.Id)) throw new InvalidOperationException("Confirmed deletion was repeated.");
            deleted.Enqueue(resource.Id);
            return Task.CompletedTask;
        }, timeProvider: clock);
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        Task? closing = null;
        try
        {
            await Task.Run(() =>
            {
                var previous = SynchronizationContext.Current;
                var continuations = new QueuedSynchronizationContext();
                Action? retry = null;
                SynchronizationContext.SetSynchronizationContext(continuations);
                try
                {
                    var acquiring = owner.AcquireAsync();
                    continuations.RunUntil(acquiring);
                    var original = acquiring.GetAwaiter().GetResult();
                    waiting = owner.AcquireAsync();
                    var returning = owner.ReturnAsync(original, (_, _) =>
                        Task.FromException(new NotSupportedException("private-reset")));
                    continuations.RunUntil(returning);
                    var reset = Assert.Throws<TtLifecycleException>(() => returning.GetAwaiter().GetResult());
                    Assert.Equal("SlotReset", reset.Stage);
                    // Recovery must reacquire the permit released by return.
                    continuations.RunUntil(clock.Requested.Task);
                    Assert.True(clock.Requested.Task.IsCompletedSuccessfully);
                    Assert.Equal((2, 2, 2), (reservations, preparations, deleted.Count));
                    Assert.False(waiting.IsCompleted);

                    // Complete the timer away from its captured context. Hold
                    // the posted retry until actual owner cancellation settles.
                    Task.Run(clock.Release).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                    retry = continuations.TakeNext();
                    closing = owner.DisposeAsync().AsTask();
                    Assert.False(closing.IsCompleted);
                    retry();
                    retry = null;
                    Assert.Equal(2, reservations);
                    Assert.Equal(2, preparations);
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                    if (retry is { } pending) ThreadPool.QueueUserWorkItem(_ => pending());
                    continuations.ReleaseToThreadPool();
                }
            });
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting!);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing!.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("SlotDispose", failure.Stage);
            Assert.Contains(nameof(IOException), failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(created.ToArray(), deleted.ToArray());
            Assert.Equal((2, 2, 0), (reservations, preparations, owner.Snapshot.Reserved));
        }
        finally
        {
            clock.Release();
            closing ??= owner.DisposeAsync().AsTask();
            if (waiting is not null) await Record.ExceptionAsync(() => waiting);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task Stopping_admission_leaves_failed_idle_cleanup_owned_without_scheduling_background_retries()
    {
        var clock = new HeldRetryClock();
        var parking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new ConcurrentQueue<Guid>();
        var deleted = new ConcurrentQueue<Guid>();
        var attempts = 0;
        Guid idleId = default;
        var owner = new TtSlotPool<Resource>(2, 1, _ =>
        {
            if (created.Count >= 2) throw new InvalidOperationException("Stopping admission created a replacement.");
            var resource = new Resource(Guid.NewGuid());
            created.Enqueue(resource.Id);
            return resource;
        }, (_, _) => Task.CompletedTask, resource =>
        {
            if (Interlocked.Increment(ref attempts) > 3) throw new InvalidOperationException("Stopped cleanup exceeded its finite oracle.");
            if (resource.Id == idleId && attempts == 1) throw new UnauthorizedAccessException("private-unconfirmed-idle-deletion");
            if (deleted.Contains(resource.Id)) throw new InvalidOperationException("Confirmed stopped deletion was repeated.");
            deleted.Enqueue(resource.Id);
            return Task.CompletedTask;
        }, timeProvider: clock);
        Task? maintenance = null;
        try
        {
            await owner.AcquireAsync();
            var idle = await owner.AcquireAsync();
            idleId = idle.Value.Id;
            await owner.ReturnAsync(idle, (_, _) => Task.CompletedTask);
            maintenance = owner.ParkIdleAsync(0, async (_, _) =>
            {
                parking.TrySetResult();
                await release.Task;
                throw new IOException("private-park-failure");
            });
            await parking.Task.WaitAsync(TimeSpan.FromSeconds(2));
            owner.StopAcceptingWork();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.AcquireAsync());
            release.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => maintenance.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("SlotPark", failure.Stage);
            Assert.Contains(nameof(IOException), failure.Message);
            Assert.Contains(nameof(UnauthorizedAccessException), failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.False(clock.Requested.Task.IsCompleted);
            Assert.Equal((2, 1, 1, 1, 0), (created.Count, attempts,
                owner.Snapshot.Leased, owner.Snapshot.Quarantined, deleted.Count));
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(created.Order(), deleted.Order());
            Assert.Equal((2, 3, 0), (created.Count, attempts, owner.Snapshot.Reserved));
            Assert.False(clock.Requested.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            clock.Release();
            if (maintenance is not null) await Record.ExceptionAsync(() => maintenance);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task An_abandoned_initial_preparation_retains_material_and_cleanup_failures_through_shutdown(
        bool materialFailure, bool failDeletion)
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new ConcurrentQueue<Guid>();
        var deleted = new ConcurrentQueue<Guid>();
        var attempts = 0;
        var owner = new TtSlotPool<Resource>(1, 1, _ =>
        {
            if (created.Count != 0) throw new InvalidOperationException("Shutdown created another initial candidate.");
            var resource = new Resource(Guid.NewGuid());
            created.Enqueue(resource.Id);
            return resource;
        }, async (_, token) =>
        {
            preparing.TrySetResult();
            await release.Task;
            if (materialFailure) throw new IOException("private-initial-preparation");
            token.ThrowIfCancellationRequested();
        }, resource =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt > (failDeletion ? 2 : 1)) throw new InvalidOperationException("Initial cleanup exceeded its finite oracle.");
            if (failDeletion && attempt == 1) throw new UnauthorizedAccessException("private-initial-deletion");
            deleted.Enqueue(resource.Id);
            return Task.CompletedTask;
        });
        using var caller = new CancellationTokenSource();
        var acquisition = owner.AcquireAsync(caller.Token);
        Task? closing = null;
        try
        {
            await preparing.Task.WaitAsync(TimeSpan.FromSeconds(2));
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
            closing = owner.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal((1, 0), (owner.Snapshot.Reserved, deleted.Count));
            release.TrySetResult();
            if (materialFailure || failDeletion)
            {
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal("SlotDispose", failure.Stage);
                Assert.Contains("SlotPrepare", failure.Message);
                Assert.Equal(materialFailure, failure.Message.Contains(nameof(IOException)));
                Assert.Equal(failDeletion, failure.Message.Contains(nameof(UnauthorizedAccessException)));
                Assert.DoesNotContain("private-", failure.ToString());
                Assert.Null(failure.InnerException);
            }
            else await closing.WaitAsync(TimeSpan.FromSeconds(2));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(created.ToArray(), deleted.ToArray());
            Assert.Equal((1, failDeletion ? 2 : 1, 0), (created.Count, attempts, owner.Snapshot.Reserved));
        }
        finally
        {
            caller.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(() => acquisition);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Later_retry_completion_settles_the_previous_material_failure_and_any_new_cleanup_failure(
        bool verifiedSuccess, bool failDeletion, bool failPreparation)
    {
        var clock = new HeldRetryClock();
        var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new ConcurrentQueue<Guid>();
        var deleted = new ConcurrentQueue<Guid>();
        var deletionAttempts = 0;
        var releaseInterval = false;
        var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            if (created.Count >= (releaseInterval ? 3 : 2))
            {
                unexpectedRetry.TrySetResult();
                throw new InvalidOperationException("Preparation exceeded its released retry budget.");
            }
            var resource = new Resource(Guid.NewGuid());
            created.Enqueue(resource.Id);
            return resource;
        }, async (_, token) =>
        {
            if (created.Count == 1) return;
            if (created.Count == 2) throw new IOException("private-earlier-preparation");
            retryEntered.TrySetResult();
            await releaseRetry.Task;
            if (failPreparation) throw new UnauthorizedAccessException("private-later-preparation");
            token.ThrowIfCancellationRequested();
        }, resource =>
        {
            var attempt = Interlocked.Increment(ref deletionAttempts);
            if (attempt > (failDeletion ? 4 : 3)) throw new InvalidOperationException("Deletion exceeded its finite oracle.");
            if (failDeletion && attempt == 3) throw new UnauthorizedAccessException("private-later-deletion");
            if (deleted.Contains(resource.Id)) throw new InvalidOperationException("Confirmed deletion was repeated.");
            deleted.Enqueue(resource.Id);
            return Task.CompletedTask;
        }, timeProvider: clock);
        using var caller = new CancellationTokenSource();
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        Task? returning = null;
        Task? closing = null;
        try
        {
            var original = await owner.AcquireAsync();
            waiting = owner.AcquireAsync(caller.Token);
            returning = owner.ReturnAsync(original, (_, _) => Task.FromException(new NotSupportedException("private-reset")));
            await Assert.ThrowsAsync<TtLifecycleException>(() => returning.WaitAsync(TimeSpan.FromSeconds(2)));
            await Task.WhenAny(clock.Requested.Task, unexpectedRetry.Task).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(unexpectedRetry.Task.IsCompleted);
            Assert.True(clock.Requested.Task.IsCompletedSuccessfully);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            releaseInterval = true;
            clock.Release();
            await retryEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((3, 2), (created.Count, deleted.Count));
            if (verifiedSuccess)
            {
                releaseRetry.TrySetResult();
                var recovered = await owner.AcquireAsync().WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(created.Last(), recovered.Value.Id);
                await owner.ReturnAsync(recovered, (_, _) => Task.CompletedTask);
                closing = owner.DisposeAsync().AsTask();
                await closing.WaitAsync(TimeSpan.FromSeconds(2));
            }
            else
            {
                closing = owner.DisposeAsync().AsTask();
                Assert.False(closing.IsCompleted);
                releaseRetry.TrySetResult();
                var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Equal("SlotDispose", failure.Stage);
                Assert.Contains(nameof(IOException), failure.Message);
                Assert.Equal(failDeletion || failPreparation, failure.Message.Contains(nameof(UnauthorizedAccessException)));
                Assert.DoesNotContain(nameof(OperationCanceledException), failure.Message);
                Assert.DoesNotContain(nameof(NotSupportedException), failure.Message);
                Assert.DoesNotContain("private-", failure.ToString());
                Assert.Null(failure.InnerException);
            }
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(created.ToArray(), deleted.ToArray());
            Assert.Equal((3, failDeletion ? 4 : 3, 0), (created.Count, deletionAttempts, owner.Snapshot.Reserved));
            Assert.False(unexpectedRetry.Task.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            clock.Release();
            releaseRetry.TrySetResult();
            if (returning is not null) await Record.ExceptionAsync(() => returning);
            if (waiting is not null) await Record.ExceptionAsync(() => waiting);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_during_replacement_retry_reports_the_unseen_preparation_failure_once(bool cancelObserver)
    {
        var clock = new HeldRetryClock();
        var unexpectedRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new ConcurrentQueue<Guid>();
        var deleted = new ConcurrentQueue<Guid>();
        var owner = new TtSlotPool<Resource>(1, 1, id =>
        {
            if (created.Count >= 2)
            {
                unexpectedRetry.TrySetResult();
                throw new InvalidOperationException("Preparation retried before its recovery interval elapsed.");
            }
            var resource = new Resource(Guid.NewGuid());
            created.Enqueue(resource.Id);
            return resource;
        }, (_, _) => created.Count == 1 ? Task.CompletedTask
            : Task.FromException(new IOException("private-replacement-preparation")), resource =>
        {
            if (deleted.Contains(resource.Id)) throw new InvalidOperationException("Confirmed deletion was repeated.");
            deleted.Enqueue(resource.Id);
            return Task.CompletedTask;
        }, timeProvider: clock);
        using var caller = new CancellationTokenSource();
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        Task? returning = null;
        Task? closing = null;
        try
        {
            var original = await owner.AcquireAsync();
            waiting = owner.AcquireAsync(caller.Token);
            returning = owner.ReturnAsync(original, (_, _) => Task.FromException(new NotSupportedException("private-reset")));
            var reset = await Assert.ThrowsAsync<TtLifecycleException>(() => returning.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("SlotReset", reset.Stage);
            await Task.WhenAny(clock.Requested.Task, unexpectedRetry.Task).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(unexpectedRetry.Task.IsCompleted);
            Assert.True(clock.Requested.Task.IsCompletedSuccessfully);
            Assert.True(await clock.Requested.Task > TimeSpan.Zero);
            Assert.Equal(created.ToArray(), deleted.ToArray());
            Assert.Equal((2, 2, 1, 1, 0), (created.Count, deleted.Count,
                owner.Snapshot.Reserved, owner.Snapshot.Quarantined, owner.Snapshot.Ready));
            Assert.False(waiting.IsCompleted);
            if (cancelObserver)
            {
                caller.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            }

            closing = owner.DisposeAsync().AsTask();
            if (!cancelObserver) await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("SlotDispose", failure.Stage);
            Assert.Contains("SlotPrepare", failure.Message);
            Assert.Contains(nameof(IOException), failure.Message);
            Assert.DoesNotContain(nameof(NotSupportedException), failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.Null(failure.InnerException);
            Assert.True(clock.Disposed);
            Assert.Equal((2, 2, 0), (created.Count, deleted.Count, owner.Snapshot.Reserved));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(created.ToArray(), deleted.ToArray());
            Assert.False(unexpectedRetry.Task.IsCompleted);
        }
        finally
        {
            caller.Cancel();
            clock.Release();
            if (returning is not null) await Record.ExceptionAsync(() => returning);
            if (waiting is not null) await Record.ExceptionAsync(() => waiting);
            if (closing is not null) await Record.ExceptionAsync(() => closing);
            await Record.ExceptionAsync(() => owner.DisposeAsync().AsTask());
            await owner.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed record Resource(Guid Id);

    private sealed class HeldRetryClock : TimeProvider
    {
        private HeldTimer? _timer;
        internal TaskCompletionSource<TimeSpan> Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed => Volatile.Read(ref _timer)?.Disposed ?? false;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new HeldTimer(callback, state);
            if (Interlocked.CompareExchange(ref _timer, timer, null) is not null)
                throw new InvalidOperationException("Recovery requested another timer before the first interval settled.");
            Requested.TrySetResult(dueTime);
            return timer;
        }

        internal void Release() => Volatile.Read(ref _timer)?.Fire();

        private sealed class HeldTimer(TimerCallback callback, object? state) : ITimer
        {
            private int _disposed;
            internal bool Disposed => Volatile.Read(ref _disposed) != 0;
            internal void Fire()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) callback(state);
            }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}

using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotPoolTests
{
    [Fact]
    public async Task Reentrant_deletion_observes_one_published_pending_drain_and_preserves_its_reservation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var confirmed = 0;
        Task? nested = null;
        TtSlotPool<object>? owner = null;
        owner = new TtSlotPool<object>(1, 1, _ => new object(), (_, _) => Task.CompletedTask, async _ =>
        {
            var attempt = Interlocked.Increment(ref attempts);
            if (attempt > 2) throw new InvalidOperationException("Deletion exceeded the finite reentry oracle.");
            if (attempt == 1)
            {
                nested = owner!.DisposeAsync().AsTask();
                entered.TrySetResult();
            }
            await release.Task;
            Interlocked.Increment(ref confirmed);
        });
        var reservation = await owner.AcquireAsync();
        await owner.ReturnAsync(reservation, (_, _) => Task.CompletedTask);
        Assert.Equal(1, owner.Snapshot.Ready);
        var outer = owner.DisposeAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 0, 1), (attempts, confirmed, owner.Snapshot.Reserved));
            Assert.Same(outer, nested);
            Assert.Same(outer, owner.DisposeAsync().AsTask());
            Assert.False(outer.IsCompleted);
            release.TrySetResult();
            await outer.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1, 0), (attempts, confirmed, owner.Snapshot.Reserved));
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => outer);
            if (nested is not null) await Record.ExceptionAsync(() => nested);
            await owner.DisposeAsync();
        }
    }

    [Fact]
    public async Task Four_blocked_preparations_reserve_capacity_before_creation_and_the_fifth_request_waits()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reserved = 0;
        var preparing = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(4, 4,
            id =>
            {
                if (Interlocked.Increment(ref reserved) > 4)
                    throw new InvalidOperationException("The capacity owner created a fifth reservation.");
                return new Resource(id);
            },
            async (_, _) =>
            {
                if (Interlocked.Increment(ref preparing) == 4) begun.TrySetResult();
                await release.Task;
            },
            resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        using var cancelFifth = new CancellationTokenSource();
        var first = Enumerable.Range(0, 4).Select(_ => owner.AcquireAsync()).ToArray();
        Task<TtSlotPool<Resource>.Reservation>? fifth = null;
        try
        {
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((4, 4, 0), (owner.Snapshot.Reserved, owner.Snapshot.Preparing, owner.Snapshot.Leased));
            fifth = owner.AcquireAsync(cancelFifth.Token);
            Assert.False(fifth.IsCompleted);
            Assert.Equal(4, reserved);
            // All five callers await a verified result, including the four preparations.
            Assert.Equal(5, owner.Snapshot.QueuedAcquisitions);
            cancelFifth.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifth);
            release.TrySetResult();
            var leases = await Task.WhenAll(first);
            Assert.Equal(4, leases.Select(lease => lease.Id).Distinct().Count());
            Assert.Equal((4, 0, 4, 0), (owner.Snapshot.Reserved, owner.Snapshot.Preparing, owner.Snapshot.Leased, owner.Snapshot.QueuedAcquisitions));
        }
        finally
        {
            cancelFifth.Cancel();
            release.TrySetResult();
            await Task.WhenAll(first);
            if (fifth is not null) await Record.ExceptionAsync(async () => await fifth);
        }
        await owner.DisposeAsync();
        Assert.Equal(4, deleted.Count);
        Assert.Equal(4, deleted.Distinct().Count());
        Assert.Equal(0, owner.Snapshot.Reserved);
    }

    [Fact]
    public async Task Returning_slots_remain_unavailable_until_bounded_reset_and_validation_finish()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executing = 0;
        await using var owner = new TtSlotPool<Resource>(2, 2, id => new Resource(id),
            (_, _) => Task.CompletedTask, _ => Task.CompletedTask);
        var first = await owner.AcquireAsync();
        var second = await owner.AcquireAsync();
        async Task Reset(Resource _, CancellationToken token)
        {
            if (Interlocked.Increment(ref executing) > 1)
                throw new InvalidOperationException("A second reset bypassed the execution limit.");
            firstStarted.TrySetResult();
            try { await release.Task.WaitAsync(token); }
            finally { Interlocked.Decrement(ref executing); }
        }
        Task? returnedFirst = null;
        Task? returnedSecond = null;
        using var cancelWaiting = new CancellationTokenSource();
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            returnedFirst = owner.ReturnAsync(first, Reset);
            Assert.False(returnedFirst.IsCompleted);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            returnedSecond = owner.ReturnAsync(second, Reset);
            Assert.Equal((2, 1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.WaitingResets,
                owner.Snapshot.ExecutingResets, owner.Snapshot.Ready));
            waiting = owner.AcquireAsync(cancelWaiting.Token);
            Assert.False(waiting.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(returnedFirst, returnedSecond).WaitAsync(TimeSpan.FromSeconds(2));
            var reassigned = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains(reassigned.Id, new[] { first.Id, second.Id });
            Assert.Equal((2, 1, 1, 0, 0), (owner.Snapshot.Reserved, owner.Snapshot.Leased,
                owner.Snapshot.Ready, owner.Snapshot.WaitingResets, owner.Snapshot.ExecutingResets));
        }
        finally
        {
            cancelWaiting.Cancel();
            release.TrySetResult();
            if (returnedFirst is not null) await returnedFirst;
            if (returnedSecond is not null) await returnedSecond;
            if (waiting is not null) await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task Unconfirmed_deletion_retains_capacity_and_fails_waiters_until_cleanup_succeeds()
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var deletionAttempts = 0;
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id => { Interlocked.Increment(ref created); return new Resource(id); },
            (_, _) => Task.CompletedTask,
            async _ =>
            {
                if (Interlocked.Increment(ref deletionAttempts) == 1)
                {
                    deleting.TrySetResult();
                    await failDeletion.Task;
                    throw new IOException("A backend cleanup failed with private details.");
                }
            });
        var leased = await owner.AcquireAsync();
        using var cancelWaiting = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancelWaiting.Token);
        Task? discarded = null;
        try
        {
            discarded = owner.DiscardAsync(leased);
            Assert.False(discarded.IsCompleted);
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Retiring, owner.Snapshot.Ready));
            Assert.False(waiting.IsCompleted);
            failDeletion.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => discarded);
            Assert.Equal("SlotDiscard", failure.Stage);
            Assert.Null(failure.InnerException);
            var queuedFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting);
            Assert.Equal("SlotDiscard", queuedFailure.Stage);
            Assert.Equal((1, 1, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.Quarantined));
            await owner.DisposeAsync();
            Assert.Equal(2, deletionAttempts);
            Assert.Equal(0, owner.Snapshot.Reserved);
        }
        finally
        {
            cancelWaiting.Cancel();
            failDeletion.TrySetResult();
            if (discarded is not null) await Record.ExceptionAsync(async () => await discarded);
            await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task Confirmed_deletion_frees_capacity_for_a_queued_replacement()
    {
        var created = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id =>
            {
                if (Interlocked.Increment(ref created) > 2)
                    throw new InvalidOperationException("More than one replacement was created.");
                return new Resource(id);
            }, (_, _) => Task.CompletedTask,
            resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        var first = await owner.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = owner.AcquireAsync(cancellation.Token);
        try
        {
            Assert.False(waiting.IsCompleted);
            await owner.DiscardAsync(first);
            var replacement = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(first.Id, replacement.Id);
            Assert.Equal(new[] { first.Id }, deleted);
            Assert.Equal((2, 1, 1), (created, owner.Snapshot.Reserved, owner.Snapshot.Leased));
        }
        finally
        {
            cancellation.Cancel();
            await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task Canceling_a_request_during_preparation_preserves_the_reserved_result_for_another_caller()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Resource? created = null;
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id => created is null ? created = new Resource(id) : throw new InvalidOperationException("A canceled preparation was replaced."),
            async (_, _) => { begun.TrySetResult(); await release.Task; }, _ => Task.CompletedTask);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = owner.AcquireAsync(firstCancellation.Token);
        Task<TtSlotPool<Resource>.Reservation>? second = null;
        try
        {
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(2));
            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Preparing, owner.Snapshot.QueuedAcquisitions));
            second = owner.AcquireAsync(secondCancellation.Token);
            Assert.False(second.IsCompleted);
            release.TrySetResult();
            var received = await second.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(created, received.Value);
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Leased, owner.Snapshot.QueuedAcquisitions));
        }
        finally
        {
            firstCancellation.Cancel();
            secondCancellation.Cancel();
            release.TrySetResult();
            await Record.ExceptionAsync(async () => await first);
            if (second is not null) await Record.ExceptionAsync(async () => await second);
        }
    }

    [Fact]
    public async Task Shutdown_rejects_waiters_and_settles_preparation_before_confirmed_deletion()
    {
        var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, id => new Resource(id),
            async (_, _) => { begun.TrySetResult(); await release.Task; },
            resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        var preparing = owner.AcquireAsync();
        await begun.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = owner.DisposeAsync().AsTask();
        try
        {
            Assert.False(closing.IsCompleted);
            Assert.Empty(deleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => preparing);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.AcquireAsync());
            release.TrySetResult();
            await closing.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Single(deleted);
            Assert.Equal(0, owner.Snapshot.Reserved);
        }
        finally { release.TrySetResult(); await closing; }
    }

    private sealed record Resource(Guid Id);

    [Fact]
    public async Task Failed_preparation_and_compensation_keep_capacity_until_confirmed_cleanup()
    {
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var attempts = 0;
        var failDeletion = true;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id => { Interlocked.Increment(ref created); return new Resource(id); },
            async (_, _) => { preparing.TrySetResult(); await release.Task; throw new IOException("prepare-private"); },
            resource =>
            {
                Interlocked.Increment(ref attempts);
                if (failDeletion) throw new IOException("cleanup-private");
                deleted.Add(resource.Id);
                return Task.CompletedTask;
            });
        var first = owner.AcquireAsync();
        await preparing.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var queued = owner.AcquireAsync();
        try
        {
            Assert.False(queued.IsCompleted);
            release.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
            var queuedFailure = await Assert.ThrowsAsync<TtLifecycleException>(() => queued.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("SlotPrepare", failure.Stage);
            Assert.Equal(failure.Message, queuedFailure.Message);
            Assert.Contains("Cleanup", failure.Message);
            Assert.DoesNotContain("private", failure.ToString());
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Quarantined, owner.Snapshot.Ready));
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.AcquireAsync());
            Assert.Equal((1, 1, 0), (created, attempts, deleted.Count));
            failDeletion = false;
            await owner.DisposeAsync();
            await owner.DisposeAsync();
            Assert.Equal((0, 2, 1), (owner.Snapshot.Reserved, attempts, deleted.Count));
        }
        finally
        {
            failDeletion = false;
            release.TrySetResult();
            await Record.ExceptionAsync(() => first);
            await Record.ExceptionAsync(() => queued);
        }
    }

    [Fact]
    public async Task Failed_preparations_share_the_cleanup_limit_before_releasing_reservations()
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(2, 2, id => new Resource(id),
            (_, _) => Task.FromException(new IOException("Preparation failed.")), async resource =>
            {
                if (Interlocked.Increment(ref active) > 1)
                    throw new InvalidOperationException("Preparation compensation exceeded the cleanup limit.");
                deleting.TrySetResult();
                try { await release.Task; deleted.Add(resource.Id); }
                finally { Interlocked.Decrement(ref active); }
            });
        var first = owner.AcquireAsync();
        Task<TtSlotPool<Resource>.Reservation>? second = null;
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            second = owner.AcquireAsync();
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            Assert.Equal((2, 1), (owner.Snapshot.Reserved, active));
            release.TrySetResult();
            await Assert.ThrowsAsync<TtLifecycleException>(() => first);
            await Assert.ThrowsAsync<TtLifecycleException>(() => second);
            await owner.DisposeAsync();
            Assert.Equal(2, deleted.Count);
            Assert.Equal(2, deleted.Distinct().Count());
            Assert.Equal(0, owner.Snapshot.Reserved);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => first);
            if (second is not null) await Record.ExceptionAsync(() => second);
        }
    }

    [Fact]
    public async Task A_failed_reset_keeps_its_execution_permit_until_compensating_cleanup_finishes()
    {
        var deleting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid failedId = default;
        await using var owner = new TtSlotPool<Resource>(2, 2, id => new Resource(id),
            (_, _) => Task.CompletedTask, async resource =>
            {
                if (resource.Id != failedId) return;
                deleting.TrySetResult();
                await release.Task;
            });
        var first = await owner.AcquireAsync();
        var second = await owner.AcquireAsync();
        failedId = first.Id;
        var firstReturn = owner.ReturnAsync(first, (_, _) => Task.FromException(new IOException("Reset failed.")));
        Task? secondReturn = null;
        try
        {
            await deleting.Task.WaitAsync(TimeSpan.FromSeconds(2));
            secondReturn = owner.ReturnAsync(second, (_, _) => { secondReset.TrySetResult(); return Task.CompletedTask; });
            Assert.False(secondReset.Task.IsCompleted);
            Assert.Equal((1, 1, 2), (owner.Snapshot.Retiring, owner.Snapshot.WaitingResets, owner.Snapshot.Reserved));
            release.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => firstReturn);
            Assert.Equal("SlotReset", failure.Stage);
            await secondReturn.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(secondReset.Task.IsCompleted);
            Assert.Equal((2, 2), (owner.Snapshot.Reserved, owner.Snapshot.Ready));
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => firstReturn);
            if (secondReturn is not null) await Record.ExceptionAsync(() => secondReturn);
        }
    }
}

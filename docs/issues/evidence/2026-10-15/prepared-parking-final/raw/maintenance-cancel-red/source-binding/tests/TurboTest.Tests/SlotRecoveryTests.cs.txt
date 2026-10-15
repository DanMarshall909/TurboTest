using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class SlotRecoveryTests
{
    [Theory]
    [InlineData("reset")]
    [InlineData("replacement")]
    [InlineData("deletion")]
    public async Task Shutdown_settles_pending_lifecycle_work_before_deletion_without_publishing_new_capacity(string phase)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        var deleteAttempts = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1,
            _ =>
            {
                if (++created > 2) throw new InvalidOperationException("Shutdown started another replacement.");
                return new Resource(Guid.NewGuid());
            }, async (_, _) =>
            {
                if (phase != "replacement" || created == 1) return;
                entered.TrySetResult();
                await release.Task;
            }, async resource =>
            {
                if (++deleteAttempts > 2) throw new InvalidOperationException("Cleanup repeated a confirmed deletion.");
                if (phase == "deletion")
                {
                    if (deleteAttempts == 1) throw new IOException("Deletion is unconfirmed.");
                    entered.TrySetResult();
                    await release.Task;
                }
                deleted.Add(resource.Id);
            });
        var original = await owner.AcquireAsync();
        var waiting = owner.AcquireAsync();
        var returning = owner.ReturnAsync(original, async (_, _) =>
        {
            if (phase != "reset") throw new IOException("Reset failed.");
            entered.TrySetResult();
            await release.Task;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var closing = owner.DisposeAsync().AsTask();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => waiting);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => owner.AcquireAsync());
            Assert.False(closing.IsCompleted);
            Assert.Equal((1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready));
            Assert.Equal(phase == "replacement" ? new[] { original.Value.Id } : Array.Empty<Guid>(), deleted);
            release.TrySetResult();
            if (phase == "reset") await returning.WaitAsync(TimeSpan.FromSeconds(2));
            else await Assert.ThrowsAsync<TtLifecycleException>(() => returning);
            await closing.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(phase == "replacement" ? 2 : 1, created);
            Assert.Equal(created, deleted.Count);
            Assert.Equal(created, deleted.Distinct().Count());
            Assert.Contains(original.Value.Id, deleted);
            Assert.Equal((0, 0, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.QueuedAcquisitions));
            await owner.DisposeAsync();
            Assert.Equal(created, deleted.Count);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => returning);
            await closing;
        }
    }

    [Fact]
    public async Task Cleanup_retries_do_not_restart_the_three_candidate_recovery_budget()
    {
        var replacementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = 0;
        Resource? dirtyResource = null;
        var deleteAttempts = new Dictionary<Guid, int>();
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1,
            _ =>
            {
                if (Interlocked.Increment(ref created) > 4)
                    throw new InvalidOperationException("Recovery restarted its three-candidate budget.");
                return new Resource(Guid.NewGuid());
            }, async (resource, token) =>
            {
                if (dirtyResource is null) return;
                replacementStarted.TrySetResult();
                await failValidation.Task.WaitAsync(token);
                throw new IOException("Candidate validation failed.");
            }, resource =>
            {
                if (!ReferenceEquals(resource, dirtyResource))
                {
                    deleteAttempts.TryGetValue(resource.Id, out var attempts);
                    deleteAttempts[resource.Id] = attempts + 1;
                    if (attempts == 0) throw new IOException("Candidate deletion is unconfirmed.");
                }
                deleted.Add(resource.Id);
                return Task.CompletedTask;
            });
        var dirty = await owner.AcquireAsync();
        dirtyResource = dirty.Value;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var waiting = owner.AcquireAsync(deadline.Token);
        try
        {
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("Reset failed."))));
            await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            failValidation.TrySetResult();
            var terminal = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting);
            Assert.Equal(4, created);
            Assert.Equal("SlotPrepare", terminal.Stage);
            Assert.Equal(4, deleted.Count);
            Assert.Equal(4, deleted.Distinct().Count());
            Assert.Equal(new[] { 2, 2, 2 }, deleteAttempts.Values.Order().ToArray());
            Assert.Equal((0, 0, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.QueuedAcquisitions));
        }
        finally
        {
            deadline.Cancel();
            failValidation.TrySetResult();
            await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task Repeated_drop_failure_exhausts_three_attempts_and_retains_the_owned_resource()
    {
        var attempts = 0;
        var created = 0;
        var fail = true;
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id => { Interlocked.Increment(ref created); return new Resource(id); },
            (_, _) => Task.CompletedTask,
            _ => { Interlocked.Increment(ref attempts); return fail ? Task.FromException(new IOException("Unconfirmed delete.")) : Task.CompletedTask; });
        var dirty = await owner.AcquireAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var waiting = owner.AcquireAsync(deadline.Token);
        try
        {
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("Reset failed."))));
            var terminal = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting);
            Assert.Equal("SlotReset", terminal.Stage);
            Assert.Contains("Cleanup", terminal.Message);
            Assert.Equal((3, 1, 1, 1, 0), (attempts, created, owner.Snapshot.Reserved,
                owner.Snapshot.Quarantined, owner.Snapshot.Ready));
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.AcquireAsync());
            Assert.Equal(3, attempts);
            fail = false;
            await owner.DisposeAsync();
            Assert.Equal((4, 0), (attempts, owner.Snapshot.Reserved));
        }
        finally
        {
            fail = false;
            deadline.Cancel();
            await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task An_unconfirmed_drop_is_retried_without_releasing_capacity_or_failing_waiting_callers()
    {
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmDeletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var created = 0;
        Resource? dirtyResource = null;
        await using var owner = new TtSlotPool<Resource>(1, 1,
            id =>
            {
                if (Interlocked.Increment(ref created) > 2) throw new InvalidOperationException("Recovery exceeded one replacement.");
                return new Resource(id);
            }, (_, _) => Task.CompletedTask, async resource =>
            {
                if (!ReferenceEquals(resource, dirtyResource)) return;
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt > 2) throw new InvalidOperationException("Confirmed deletion was retried again.");
                if (attempt == 1) throw new IOException("Deletion is unconfirmed.");
                retryStarted.TrySetResult();
                await confirmDeletion.Task;
            });
        var dirty = await owner.AcquireAsync();
        dirtyResource = dirty.Value;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var waiting = owner.AcquireAsync(deadline.Token);
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("Reset failed."))));
            Assert.Equal("SlotReset", failure.Stage);
            Assert.Contains("Cleanup", failure.Message);
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(waiting.IsCompleted);
            Assert.Equal((1, 1, 1), (owner.Snapshot.Reserved, owner.Snapshot.Retiring, created));
            confirmDeletion.TrySetResult();
            var clean = await waiting;
            Assert.NotEqual(dirty.Id, clean.Id);
            Assert.Equal((1, 1, 2, 2), (owner.Snapshot.Reserved, owner.Snapshot.Leased, created, attempts));
        }
        finally
        {
            deadline.Cancel();
            confirmDeletion.TrySetResult();
            await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task Three_failed_recovery_preparations_settle_waiters_with_a_terminal_diagnostic()
    {
        var recoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparations = 0;
        var deleted = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, _ => new Resource(Guid.NewGuid()),
            async (_, token) =>
            {
                var attempt = Interlocked.Increment(ref preparations);
                if (attempt > 4) throw new InvalidOperationException("Recovery exceeded three preparation attempts.");
                if (attempt == 1) return;
                recoveryStarted.TrySetResult();
                await failRecovery.Task.WaitAsync(token);
                throw new IOException("Every recovery candidate is invalid.");
            }, resource => { deleted.Add(resource.Id); return Task.CompletedTask; });
        var dirty = await owner.AcquireAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("Reset failed."))));
            await recoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            waiting = owner.AcquireAsync(deadline.Token);
            failRecovery.TrySetResult();
            var terminal = await Assert.ThrowsAsync<TtLifecycleException>(() => waiting);
            Assert.Equal("SlotPrepare", terminal.Stage);
            Assert.Contains(nameof(IOException), terminal.Message);
            Assert.Equal(4, preparations);
            Assert.Equal(4, deleted.Count);
            Assert.Equal(4, deleted.Distinct().Count());
            Assert.Equal((0, 0, 0), (owner.Snapshot.Reserved, owner.Snapshot.Ready, owner.Snapshot.QueuedAcquisitions));
            var next = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.AcquireAsync());
            Assert.Equal(terminal.Message, next.Message);
            Assert.Equal(4, preparations);
        }
        finally
        {
            deadline.Cancel();
            failRecovery.TrySetResult();
            if (waiting is not null) await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task A_corrupt_recovery_candidate_is_deleted_and_a_verified_retry_restores_capacity()
    {
        var badCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rejectCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goodCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validateCandidate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparations = 0;
        Resource? invalid = null;
        var deletions = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, _ => new Resource(Guid.NewGuid()),
            async (resource, token) =>
            {
                var attempt = Interlocked.Increment(ref preparations);
                if (attempt > 3) throw new InvalidOperationException("Recovery exceeded its expected finite attempts.");
                if (attempt == 2)
                {
                    invalid = resource;
                    badCandidate.TrySetResult();
                    await rejectCandidate.Task.WaitAsync(token);
                    throw new IOException("Replacement validation failed.");
                }
                if (attempt == 3)
                {
                    goodCandidate.TrySetResult();
                    await validateCandidate.Task.WaitAsync(token);
                }
            }, resource => { deletions.Add(resource.Id); return Task.CompletedTask; });
        var dirty = await owner.AcquireAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<TtSlotPool<Resource>.Reservation>? waiting = null;
        try
        {
            await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("Reset failed."))));
            await badCandidate.Task.WaitAsync(TimeSpan.FromSeconds(2));
            waiting = owner.AcquireAsync(deadline.Token);
            Assert.False(waiting.IsCompleted);
            rejectCandidate.TrySetResult();
            await goodCandidate.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { dirty.Value.Id, invalid!.Id }, deletions);
            Assert.Equal((1, 1, 0, 1), (owner.Snapshot.Reserved, owner.Snapshot.Preparing,
                owner.Snapshot.Ready, owner.Snapshot.QueuedAcquisitions));
            Assert.False(waiting.IsCompleted);
            validateCandidate.TrySetResult();
            var clean = await waiting;
            Assert.NotEqual(dirty.Value.Id, clean.Value.Id);
            Assert.NotEqual(invalid.Id, clean.Value.Id);
            Assert.Equal(3, preparations);
        }
        finally
        {
            deadline.Cancel();
            rejectCandidate.TrySetResult();
            validateCandidate.TrySetResult();
            if (waiting is not null) await Record.ExceptionAsync(async () => await waiting);
        }
    }

    [Fact]
    public async Task A_failed_reset_restores_verified_capacity_even_without_a_waiting_acquisition()
    {
        var replacementStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var verifyReplacement = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparations = 0;
        var deletions = new List<Guid>();
        await using var owner = new TtSlotPool<Resource>(1, 1, id => new Resource(id),
            async (_, token) =>
            {
                var attempt = Interlocked.Increment(ref preparations);
                if (attempt > 2) throw new InvalidOperationException("Recovery created more than one replacement.");
                if (attempt == 2)
                {
                    replacementStarted.TrySetResult();
                    await verifyReplacement.Task.WaitAsync(token);
                }
            }, resource => { deletions.Add(resource.Id); return Task.CompletedTask; });
        var dirty = await owner.AcquireAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Task<TtSlotPool<Resource>.Reservation>? next = null;
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.ReturnAsync(dirty,
                (_, _) => Task.FromException(new IOException("The reset failed."))));
            Assert.Equal("SlotReset", failure.Stage);
            await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { dirty.Id }, deletions);
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Preparing, owner.Snapshot.Ready));
            next = owner.AcquireAsync(deadline.Token);
            Assert.False(next.IsCompleted);
            verifyReplacement.TrySetResult();
            var clean = await next;
            Assert.NotEqual(dirty.Id, clean.Id);
            Assert.Equal((1, 1, 0), (owner.Snapshot.Reserved, owner.Snapshot.Leased, owner.Snapshot.QueuedAcquisitions));
            Assert.Equal(2, preparations);
        }
        finally
        {
            deadline.Cancel();
            verifyReplacement.TrySetResult();
            if (next is not null) await Record.ExceptionAsync(async () => await next);
        }
    }

    private sealed record Resource(Guid Id);
}

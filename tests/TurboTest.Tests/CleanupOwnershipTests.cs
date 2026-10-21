using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

[Collection("Static lifecycle")]
public class CleanupOwnershipTests
{
    [Fact]
    public async Task Composed_cleanup_shares_reentrant_work_and_retries_only_the_failed_child_even_when_resource_labels_match()
    {
        var entered = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resources = new[] { new List<string> { "same-resource-label" }, new List<string> { "same-resource-label" } };
        var calls = new int[2];
        TtCleanup? combined = null;
        Task? reentrant = null;
        var children = Enumerable.Range(0, 2).Select(index => new TtCleanup(Guid.NewGuid(), async () =>
        {
            var attempt = ++calls[index];
            if (attempt > (index == 0 ? 1 : 2)) throw new InvalidOperationException("Child recovery exceeded its finite oracle.");
            reentrant = combined!.RetryCleanupAsync();
            entered[index].TrySetResult();
            await release.Task;
            if (index == 1 && attempt == 1)
                throw new TtLifecycleException("RemainingChildDelete",
                    new Npgsql.PostgresException("private-child-delete", "ERROR", "ERROR", "53100"));
            resources[index].Clear();
        }, () => resources[index])).ToArray();
        var causal = new TtLifecycleException("FirstChild", new IOException("private-first"), cleanup: children[0]);
        var secondary = new TtLifecycleException("SecondChild", new IOException("private-second"), cleanup: children[1]);
        combined = Assert.IsType<TtCleanup>(new TtLifecycleException("Outer",
            new AggregateException(causal, new AggregateException(secondary, causal))).Cleanup);
        var pending = combined.RetryCleanupAsync();
        try
        {
            await Task.WhenAll(entered.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(pending, combined.RetryCleanupAsync());
            Assert.Same(pending, reentrant);
            Assert.False(pending.IsCompleted);
            Assert.Equal(new[] { 1, 1 }, calls);
            Assert.Equal(["same-resource-label"], combined.OwnedResourceIds);
            release.TrySetResult();
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Contains("RemainingChildDelete", failure.Message);
            Assert.Contains("53100", failure.Message);
            Assert.DoesNotContain("private-", failure.ToString());
            Assert.NotNull(failure.Cleanup);
            Assert.Empty(children[0].OwnedResourceIds);
            Assert.Single(children[1].OwnedResourceIds);
            await combined.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            await combined.RetryCleanupAsync();
            Assert.Equal(new[] { 1, 2 }, calls);
            Assert.Empty(combined.OwnedResourceIds);
        }
        finally
        {
            release.TrySetResult();
            await Record.ExceptionAsync(() => pending);
            await combined.RetryCleanupAsync();
        }
    }

    [Fact]
    public async Task Completed_child_ownership_does_not_displace_the_remaining_recovery_capability()
    {
        var childCalls = 0;
        var child = new TtCleanup(Guid.NewGuid(), () => { childCalls++; return Task.CompletedTask; }, () => ["child-resource"]);
        var cause = new TtLifecycleException("ChildCleanup", new IOException("private-child-failure"), cleanup: child);
        await child.RetryCleanupAsync();
        var calls = 0;
        var owner = new TtCleanup(Guid.NewGuid(), () =>
        {
            if (++calls == 1) throw cause;
            if (calls > 2) throw new InvalidOperationException("Recovery exceeded its finite oracle.");
            return Task.CompletedTask;
        }, () => ["remaining-resource"]);
        try
        {
            var failure = await Assert.ThrowsAsync<TtLifecycleException>(() => owner.RetryCleanupAsync());
            var remaining = Assert.IsType<TtCleanup>(failure.Cleanup);
            Assert.Same(owner, remaining);
            Assert.Equal(["remaining-resource"], remaining.OwnedResourceIds);
            Assert.Equal("ChildCleanup", failure.Stage);
            Assert.DoesNotContain("private-", failure.ToString());
            await remaining.RetryCleanupAsync();
            Assert.Empty(owner.OwnedResourceIds);
            Assert.Equal((1, 2), (childCalls, calls));
        }
        finally { await owner.RetryCleanupAsync(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Distinct_explicit_causal_and_secondary_cleanup_owners_remain_recoverable_without_registering_a_new_owner(bool explicitOwner)
    {
        await Tt.DisposeAsync();
        var resources = new[] { new List<string> { "explicit-resource" }, new List<string> { "causal-resource" }, new List<string> { "secondary-resource" } };
        var calls = new int[3];
        var owners = Enumerable.Range(0, 3).Select(index => new TtCleanup(Guid.NewGuid(), () =>
        {
            calls[index]++;
            resources[index].Clear();
            return Task.CompletedTask;
        }, () => resources[index])).ToArray();
        try
        {
            var cause = new TtLifecycleException("CausalStage", new IOException("private-cause"), cleanup: owners[1]);
            var secondary = new TtLifecycleException("SecondaryStage", new InvalidOperationException("private-secondary"), cleanup: owners[2]);
            var failure = new TtLifecycleException("OuterStage", cause, secondary, explicitOwner ? owners[0] : null);
            var capability = Assert.IsType<TtCleanup>(failure.Cleanup);
            var expected = explicitOwner ? new[] { "causal-resource", "explicit-resource", "secondary-resource" } : new[] { "causal-resource", "secondary-resource" };
            Assert.Equal(expected, capability.OwnedResourceIds.Order(StringComparer.Ordinal));
            var supplemented = failure.WithCleanupFailure(secondary);
            Assert.Same(capability, supplemented.Cleanup);
            Assert.Same(capability, supplemented.WithCleanup(owners[explicitOwner ? 0 : 1]).Cleanup);
            Assert.DoesNotContain("private-", supplemented.ToString());
            await Tt.DisposeAsync();
            Assert.Equal(new[] { 0, 0, 0 }, calls);
            await capability.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(new[] { explicitOwner ? 1 : 0, 1, 1 }, calls);
            Assert.Empty(capability.OwnedResourceIds);
        }
        finally
        {
            foreach (var owner in owners) await owner.RetryCleanupAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Global_cleanup_reports_each_failed_owner_and_returns_a_capability_that_retries_them_all(bool reverse)
    {
        await Tt.DisposeAsync();
        var firstResources = new List<string> { "first-retained-resource" };
        var secondResources = new List<string> { "second-retained-resource" };
        var firstCalls = 0;
        var secondCalls = 0;
        var finish = false;
        TtCleanup RetainFirst() => TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            firstCalls++;
            if (!finish && firstCalls == 1)
                throw new TtLifecycleException("FirstOwnerDelete",
                    new Npgsql.PostgresException("private-first-delete", "ERROR", "ERROR", "53100"));
            firstResources.Clear();
            return Task.CompletedTask;
        }, () => firstResources);
        TtCleanup RetainSecond() => TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            secondCalls++;
            if (!finish && secondCalls == 1)
                throw new TtLifecycleException("SecondOwnerDelete", new IOException("private-second-delete"));
            secondResources.Clear();
            return Task.CompletedTask;
        }, () => secondResources);
        var second = reverse ? RetainSecond() : null;
        var first = RetainFirst();
        second ??= RetainSecond();
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal((1, 1), (firstCalls, secondCalls));
            Assert.Contains("FirstOwnerDelete", error.Message);
            Assert.Contains("SecondOwnerDelete", error.Message);
            Assert.Contains("53100", error.Message);
            Assert.Contains(nameof(IOException), error.Message);
            Assert.DoesNotContain("private-", error.ToString());
            Assert.Null(error.InnerException);
            var cleanup = Assert.IsType<TtCleanup>(error.Cleanup);
            Assert.Equal(new[] { "first-retained-resource", "second-retained-resource" },
                cleanup.OwnedResourceIds.Order(StringComparer.Ordinal));
            await cleanup.RetryCleanupAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal((2, 2), (firstCalls, secondCalls));
            Assert.Empty(firstResources);
            Assert.Empty(secondResources);
            Assert.Empty(cleanup.OwnedResourceIds);
            await cleanup.RetryCleanupAsync();
            await Tt.DisposeAsync();
            Assert.Equal((2, 2), (firstCalls, secondCalls));
        }
        finally
        {
            finish = true;
            await first.RetryCleanupAsync();
            await second.RetryCleanupAsync();
            await Tt.DisposeAsync();
        }
    }

    [Fact]
    public async Task Global_cleanup_bounds_its_wait_and_keeps_pending_work_for_a_later_drain()
    {
        await Tt.DisposeAsync();
        Tt.Configure(options => options.DrainTimeout = TimeSpan.FromMilliseconds(100));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var resources = new List<string> { "pending-owned-resource" };
        var cleanup = TtCleanupRegistry.Retain(Guid.NewGuid(), async () =>
        {
            if (++calls > 1) throw new InvalidOperationException("Pending cleanup was restarted.");
            started.TrySetResult();
            await release.Task;
            resources.Clear();
        }, () => resources);
        var otherCompleted = false;
        var other = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            otherCompleted = true;
            return Task.CompletedTask;
        }, () => ["other-owned-resource"]);
        try
        {
            var draining = Tt.DisposeAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => draining.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("DrainTimeout", error.Stage);
            Assert.Same(cleanup, error.Cleanup);
            Assert.Equal(["pending-owned-resource"], cleanup.OwnedResourceIds);
            Assert.True(otherCompleted);
            Assert.Empty(other.OwnedResourceIds);
            var pending = cleanup.RetryCleanupAsync();
            Assert.False(pending.IsCompleted);
            release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(2));
            await Tt.DisposeAsync();
            Assert.Equal(1, calls);
            Assert.Empty(cleanup.OwnedResourceIds);
        }
        finally
        {
            release.TrySetResult();
            await cleanup.RetryCleanupAsync();
            await other.RetryCleanupAsync();
            await Tt.DisposeAsync();
            Tt.Configure(options => options.DrainTimeout = TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Reentrant_cleanup_observes_the_same_pending_attempt_without_repeating_the_callback()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task? nested = null;
        TtCleanup? cleanup = null;
        cleanup = new TtCleanup(Guid.NewGuid(), async () =>
        {
            if (++calls > 1) throw new InvalidOperationException("Cleanup callback was repeated.");
            nested = cleanup!.RetryCleanupAsync();
            entered.TrySetResult();
            await release.Task;
        }, () => ["held-resource"]);
        var attempt = cleanup.RetryCleanupAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Same(attempt, nested);
            Assert.False(attempt.IsCompleted);
            Assert.Equal(["held-resource"], cleanup.OwnedResourceIds);
        }
        finally { release.TrySetResult(); }
        await attempt.WaitAsync(TimeSpan.FromSeconds(2));
        await cleanup.RetryCleanupAsync();
        Assert.Equal(1, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
    }

    [Fact]
    public async Task Global_cleanup_continues_after_one_failure_and_retains_only_the_failed_owner()
    {
        var fail = true;
        var retained = new List<string> { "first-owned-resource" };
        var other = new List<string> { "second-owned-resource" };
        var first = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            if (fail) throw new IOException("private-delete-detail");
            retained.Clear();
            return Task.CompletedTask;
        }, () => retained);
        var second = TtCleanupRegistry.Retain(Guid.NewGuid(), () =>
        {
            other.Clear();
            return Task.CompletedTask;
        }, () => other);
        try
        {
            var error = await Assert.ThrowsAsync<TtLifecycleException>(() => Tt.DisposeAsync());
            Assert.Same(first, error.Cleanup);
            Assert.DoesNotContain("private-delete-detail", error.ToString());
            Assert.Equal(["first-owned-resource"], first.OwnedResourceIds);
            Assert.Empty(second.OwnedResourceIds);
            fail = false;
            await Tt.DisposeAsync();
            Assert.Empty(first.OwnedResourceIds);
        }
        finally
        {
            fail = false;
            await first.RetryCleanupAsync();
            await second.RetryCleanupAsync();
        }
    }

    [Fact]
    public async Task Concurrent_cleanup_shares_one_attempt_and_retains_accounting_until_completion()
    {
        var resources = new List<string> { "resource-first", "resource-second" };
        var permit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cleanup = new TtCleanup(Guid.NewGuid(), async () =>
        {
            calls++;
            started.SetResult();
            await permit.Task;
            resources.Clear();
        }, () => resources);
        var snapshot = cleanup.OwnedResourceIds;
        var first = cleanup.RetryCleanupAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var concurrent = cleanup.RetryCleanupAsync();
        try
        {
            Assert.Same(first, concurrent);
            Assert.False(first.IsCompleted);
            Assert.Equal(["resource-first", "resource-second"], cleanup.OwnedResourceIds);
        }
        finally { permit.TrySetResult(); }
        await Task.WhenAll(first, concurrent).WaitAsync(TimeSpan.FromSeconds(2));
        await cleanup.RetryCleanupAsync();
        Assert.Equal(1, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
        Assert.Equal(["resource-first", "resource-second"], snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_retry_keeps_safe_diagnostics_and_the_capability_for_another_attempt(bool lifecycle)
    {
        var fail = true;
        var calls = 0;
        var resources = new List<string> { "retained-resource" };
        var cleanup = new TtCleanup(Guid.NewGuid(), () =>
        {
            calls++;
            if (fail)
            {
                var cause = new IOException("cleanup-secret");
                throw lifecycle ? new TtLifecycleException("OwnedHostDelete", cause) : cause;
            }
            resources.Clear();
            return Task.CompletedTask;
        }, () => resources);
        var error = await Assert.ThrowsAsync<TtLifecycleException>(() => cleanup.RetryCleanupAsync());
        Assert.Equal(lifecycle ? "OwnedHostDelete" : "FailedStartupCleanup", error.Stage);
        Assert.DoesNotContain("cleanup-secret", error.ToString());
        Assert.Same(cleanup, error.Cleanup);
        var supplemented = error.WithCleanupFailure(new IOException("another-secret"));
        Assert.Same(cleanup, supplemented.Cleanup);
        Assert.DoesNotContain("secret", supplemented.ToString());
        Assert.Equal(["retained-resource"], cleanup.OwnedResourceIds);
        fail = false;
        await cleanup.RetryCleanupAsync();
        await cleanup.RetryCleanupAsync();
        Assert.Equal(2, calls);
        Assert.Empty(cleanup.OwnedResourceIds);
    }
}

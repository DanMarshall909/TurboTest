using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class PreparationBudgetTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Configured_preparation_and_reset_bounds_govern_actual_executing_work()
    {
        var preparationsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreparations = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resetsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResets = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparing = 0;
        var resetting = 0;
        await using var pool = new TtPreparedPool<TestDbContext>(new TtOptions
        {
            PoolSize = 4, PreparationConcurrency = 2, ResetConcurrency = 2
        }, new TtBaselineDefinition<TestDbContext>("executors-v1", TtSchemaInitialization.EnsureCreated),
            beforeVerification: async _ =>
            {
                var active = Interlocked.Increment(ref preparing);
                if (active > 2) throw new InvalidOperationException("Preparation exceeded its configured bound.");
                if (active == 2) preparationsStarted.TrySetResult();
                try { await releasePreparations.Task; }
                finally { Interlocked.Decrement(ref preparing); }
            }, afterSequenceRestore: async (_, _) =>
            {
                var active = Interlocked.Increment(ref resetting);
                if (active > 2) throw new InvalidOperationException("Reset exceeded its configured bound.");
                if (active == 2) resetsStarted.TrySetResult();
                try { await releaseResets.Task; }
                finally { Interlocked.Decrement(ref resetting); }
            });
        var initialization = pool.InitializeAsync(default);
        var leases = new List<TtDatabase>();
        var returns = new List<Task>();
        try
        {
            await preparationsStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(2, preparing);
            Assert.Equal((4, 4, 0), (pool.Snapshot.Reserved, pool.Snapshot.Preparing, pool.Snapshot.Ready));
            releasePreparations.TrySetResult();
            await initialization;
            for (var i = 0; i < 3; i++) leases.Add(await pool.GetDatabaseAsync());
            returns.Add(leases[0].DisposeAsync().AsTask());
            returns.Add(leases[1].DisposeAsync().AsTask());
            await resetsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            returns.Add(leases[2].DisposeAsync().AsTask());
            Assert.Equal((4, 2, 1, 1), (pool.Snapshot.Reserved, pool.Snapshot.ExecutingResets,
                pool.Snapshot.WaitingResets, pool.Snapshot.Ready));
            releaseResets.TrySetResult();
            await Task.WhenAll(returns);
            Assert.Equal((4, 4, 0, 0), (pool.Snapshot.Reserved, pool.Snapshot.Ready,
                pool.Snapshot.WaitingResets, pool.Snapshot.ExecutingResets));
        }
        finally
        {
            releasePreparations.TrySetResult();
            releaseResets.TrySetResult();
            await initialization;
            foreach (var lease in leases) await lease.DisposeAsync();
        }
    }
}

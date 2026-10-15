using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class OrdinaryCapacityTests
{
    [Fact]
    public async Task Legacy_dynamic_scaling_cannot_bypass_the_fixed_capacity_owner()
    {
        TtPool? pool = null;
        try
        {
            var error = Assert.Throws<ArgumentException>(() =>
                pool = new TtPool(new TtOptions { PoolSize = 1, EnableDynamicScaling = true }));
            Assert.Equal(nameof(TtOptions.EnableDynamicScaling), error.ParamName);
        }
        finally { if (pool is not null) await pool.DisposeAsync(); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_ordinary_fixture_waits_at_its_container_cap_and_reuses_the_warm_session()
    {
        var creations = 0;
        var options = new TtOptions { PoolSize = 1 };
        await using var pool = new TtPool(options, beforeStart: _ =>
        {
            if (Interlocked.Increment(ref creations) > 1)
                throw new InvalidOperationException("An overflow container bypassed the physical cap.");
            return Task.CompletedTask;
        });
        await pool.InitializeAsync();
        await using var fixture = new TtFactory<TestDbContext>(pool, options);
        await using var first = await fixture.CreateLeaseAsync();
        Assert.Equal((1, 1, 1), (fixture.PoolState.SlotLimit, fixture.PoolState.Reserved, fixture.PoolState.Leased));
        var identity = await PooledResetTests.ReadIdentityAsync(first.Context);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CreateLeaseAsync(
            cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, creations);
        using var nextCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var waiting = fixture.CreateLeaseAsync(cancellationToken: nextCancellation.Token);
        Assert.False(waiting.IsCompleted);
        Assert.Equal((1, 1), (fixture.PoolState.Reserved, fixture.PoolState.QueuedAcquisitions));
        await first.DisposeAsync();
        await using var reused = await waiting;
        Assert.Equal(identity, await PooledResetTests.ReadIdentityAsync(reused.Context));
        Assert.Equal(1, creations);
    }
}

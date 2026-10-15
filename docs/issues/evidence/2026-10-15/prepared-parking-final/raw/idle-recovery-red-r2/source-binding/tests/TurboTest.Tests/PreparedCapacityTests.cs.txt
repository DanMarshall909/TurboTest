using Microsoft.EntityFrameworkCore;
using TurboTest.EFCore;

namespace TurboTest.Tests;

public class PreparedCapacityTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Four_exclusive_databases_isolate_markers_and_a_fifth_waits_for_verified_return()
    {
        var clones = 0;
        await using var fixture = await Tt.CreatePreparedFactoryAsync(new TtOptions { PoolSize = 4, MaxContainers = 1 },
            new TtBaselineDefinition<TestDbContext>("capacity-v1", TtSchemaInitialization.EnsureCreated), default,
            beforeVerification: _ =>
            {
                if (Interlocked.Increment(ref clones) > 4)
                    throw new InvalidOperationException("A fifth physical database bypassed the reservation cap.");
                return Task.CompletedTask;
            });
        var leases = new List<TtLease<TestDbContext>>();
        try
        {
            Assert.Equal((4, 4, 4), (fixture.PoolState.SlotLimit, fixture.PoolState.Reserved, fixture.PoolState.Ready));
            Assert.Equal((4, 1, 6L), (fixture.ResourceBudget.WorkloadSlotLimit,
                fixture.ResourceBudget.ContainerLimit, fixture.ResourceBudget.MaximumOwnedDatabases));
            var identities = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var lease = await fixture.CreateLeaseAsync();
                leases.Add(lease);
                identities.Add(await lease.Context.Database.SqlQueryRaw<string>(
                    "SELECT current_database() AS \"Value\"").SingleAsync());
                lease.Context.Users.Add(new User { Email = $"slot-{i}@example.com" });
                await lease.Context.SaveChangesAsync();
            }
            Assert.Equal(4, identities.Distinct().Count());
            Assert.Equal((4, 4, 0), (fixture.PoolState.Reserved, fixture.PoolState.Leased, fixture.PoolState.Ready));
            for (var i = 0; i < 4; i++)
            {
                var markers = await leases[i].Context.Users.Select(user => user.Email).ToListAsync();
                Assert.Equal(new[] { $"slot-{i}@example.com" }, markers);
                foreach (var other in Enumerable.Range(0, 4).Where(index => index != i))
                    Assert.DoesNotContain($"slot-{other}@example.com", markers);
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CreateLeaseAsync(
                cancellationToken: cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(4, clones);
            Assert.Equal(0, fixture.PoolState.QueuedAcquisitions);
            using var nextCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var waiting = fixture.CreateLeaseAsync(cancellationToken: nextCancellation.Token);
            Assert.False(waiting.IsCompleted);
            Assert.Equal((4, 1), (fixture.PoolState.Reserved, fixture.PoolState.QueuedAcquisitions));
            await leases[0].DisposeAsync();
            await using var replacement = await waiting;
            Assert.Equal(identities[0], await replacement.Context.Database.SqlQueryRaw<string>(
                "SELECT current_database() AS \"Value\"").SingleAsync());
            Assert.Empty(await replacement.Context.Users.ToListAsync());
            Assert.Equal(4, clones);
        }
        finally
        {
            foreach (var lease in leases) await lease.DisposeAsync();
        }
    }
}

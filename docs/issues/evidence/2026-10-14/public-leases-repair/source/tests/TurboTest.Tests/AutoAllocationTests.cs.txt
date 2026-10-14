using TurboTest.EFCore;

namespace TurboTest.Tests;

public class AutoAllocationTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Auto_allocated_fixture_supports_reusable_public_leases()
    {
        await using var fixture = await Tt.CreateFactoryAsync<TestDbContext>(o =>
        {
            o.AutoAllocateResources = true;
            o.MaxAutoContainers = 1;
            o.ReserveMemoryMb = 2048;
            o.ReserveCpuCores = 1;
        });
        await using var first = await fixture.CreateLeaseAsync();
        var identity = await PooledResetTests.ReadIdentityAsync(first.Context);
        await first.DisposeAsync();
        await using var second = await fixture.CreateLeaseAsync();
        Assert.Equal(identity, await PooledResetTests.ReadIdentityAsync(second.Context));
    }
}

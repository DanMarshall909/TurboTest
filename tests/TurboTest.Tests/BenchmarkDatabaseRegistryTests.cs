using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkDatabaseRegistryTests
{
    [Fact]
    public async Task Concurrent_verified_growth_is_accepted_once_and_retained_identity_reuse_is_stable()
    {
        var registry = new BenchmarkDatabaseRegistry(3);
        registry.Observe(new("startup-a", 17));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => registry.Observe(new("grown-b", 29)))));
        registry.Observe(new("grown-c", 43));
        registry.Observe(new("startup-a", 17));
        Assert.Equal(new[] { new DatabaseIdentity("grown-b", 29), new("grown-c", 43), new("startup-a", 17) }, registry.Snapshot);
    }

    [Theory]
    [InlineData("startup-a", 99u, "DatabaseReuse")]
    [InlineData("alias-b", 17u, "DatabaseIdentity")]
    [InlineData("excess-d", 47u, "DatabaseCapacity")]
    public void Changed_alias_or_excess_identities_are_rejected_without_altering_the_accepted_set(string name, uint oid, string diagnostic)
    {
        var registry = new BenchmarkDatabaseRegistry(2);
        registry.Observe(new("startup-a", 17));
        registry.Observe(new("grown-b", 29));
        var failure = Assert.Throws<InvalidDataException>(() => registry.Observe(new(name, oid)));
        Assert.Contains(diagnostic, failure.Message);
        Assert.Equal(new[] { new DatabaseIdentity("grown-b", 29), new("startup-a", 17) }, registry.Snapshot);
    }
}

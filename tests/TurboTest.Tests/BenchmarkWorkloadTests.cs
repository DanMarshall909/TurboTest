using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkWorkloadTests
{
    [Theory]
    [InlineData(130, 100, 20, 10)]
    [InlineData(1300, 1000, 200, 100)]
    [InlineData(13000, 10000, 2000, 1000)]
    public void Named_workloads_define_the_literal_nonempty_row_distribution(int total, int parents, int children, int notes)
    {
        var mix = WorkloadMix.ForTotal(total);
        Assert.Equal((total, parents, children, notes), (mix.Total, mix.Parents, mix.Children, mix.Notes));
        mix.RequireObserved(parents, children, notes);
    }

    [Theory]
    [InlineData(0, 0, 0, "WorkloadEmpty")]
    [InlineData(100, 20, 0, "WorkloadDistribution")]
    [InlineData(90, 30, 10, "WorkloadDistribution")]
    [InlineData(101, 20, 10, "WorkloadDistribution")]
    public void Invalid_row_observations_are_rejected_by_name_before_timing(int parents, int children, int notes, string diagnostic)
    {
        var error = Assert.Throws<InvalidDataException>(() => WorkloadMix.ForTotal(130).RequireObserved(parents, children, notes));
        Assert.Contains(diagnostic, error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(131)]
    [InlineData(-130)]
    public void An_unsupported_row_total_cannot_silently_run_a_different_workload(int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkloadMix.ForTotal(total));
    }
}

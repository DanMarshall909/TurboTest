using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkReportTests
{
    [Fact]
    public void Report_uses_median_and_nearest_rank_p95_over_every_observation()
    {
        var even = DurationSummary.FromMilliseconds([9, 1, 7, 2]);
        Assert.Equal(new DurationSummary(4, 4.5, 9, 9), even);
        var twenty = DurationSummary.FromMilliseconds(Enumerable.Range(1, 20).Reverse().Select(value => (double)value));
        Assert.Equal(new DurationSummary(20, 10.5, 19, 20), twenty);
        Assert.Equal(new DurationSummary(3, 2, 9, 9), DurationSummary.FromMilliseconds([9, 1, 2]));
    }

    [Fact]
    public void No_completed_samples_report_zero_count_and_unavailable_percentiles()
    {
        Assert.Equal(new DurationSummary(0, null, null, null), DurationSummary.FromMilliseconds([]));
        Assert.Equal(new DurationSummary(1, 0, 0, 0), DurationSummary.FromMilliseconds([0]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_duration_cannot_be_hidden_by_other_successful_samples(double invalid)
    {
        var error = Assert.Throws<InvalidDataException>(() => DurationSummary.FromMilliseconds([1, invalid, 4]));
        Assert.Contains("DurationInvalid", error.Message);
    }
}

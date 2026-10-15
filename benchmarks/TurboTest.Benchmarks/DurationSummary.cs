namespace TurboTest.Benchmarks;

/// <summary>Median p50 and nearest-rank ceil(.95*n) p95, in milliseconds.</summary>
public sealed record DurationSummary(int Count, double? P50, double? P95, double? Maximum)
{
    public static DurationSummary FromMilliseconds(IEnumerable<double> values)
    {
        var samples = values.ToArray();
        if (samples.Any(value => !double.IsFinite(value) || value < 0))
            throw new InvalidDataException("DurationInvalid: durations must be finite and nonnegative.");
        if (samples.Length == 0) return new(0, null, null, null);
        Array.Sort(samples);
        var middle = samples.Length / 2;
        var median = samples.Length % 2 == 0
            ? samples[middle - 1] / 2 + samples[middle] / 2
            : samples[middle];
        return new(samples.Length, median, samples[(int)Math.Ceiling(.95 * samples.Length) - 1], samples[^1]);
    }
}

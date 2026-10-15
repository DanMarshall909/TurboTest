using TurboTest.EFCore;

namespace TurboTest.Benchmarks;

public sealed record BenchmarkPhase(string Name, int Concurrency, int[] Rows, double IdleMilliseconds = 0);
public sealed record BenchmarkCase(int Block, string Mode, int Capacity, BenchmarkPhase[] Phases, int? StartupWarmDatabases = null)
{
    public int Startup => StartupWarmDatabases ?? Capacity;
    public TtAdaptiveOptions? AdaptivePolicy => Mode == "adaptive"
        ? new() { MinimumWarmDatabases = Startup, MaximumWarmDatabases = Capacity } : null;
    public string Profile => $"{Mode}-{Capacity}" + (StartupWarmDatabases is null ? "" : $"-startup-{Startup}");
}

public static class BenchmarkSchedule
{
    private static readonly (string Name, int Concurrency, int Waves)[] Mixed =
    [
        ("single-start", 1, 1), ("first-burst", 8, 1), ("single-after", 1, 1),
        ("idle", 0, 0), ("renewed-burst", 8, 1), ("repeat-burst", 8, 1),
        ("sustained-heavy", 8, 3), ("single-final", 1, 1)
    ];

    public static BenchmarkCase[] Create(int seed, int blocks, int samples, double idleMilliseconds, bool adaptiveComparison = false)
    {
        if (blocks < 2) throw new ArgumentOutOfRangeException(nameof(blocks), "Comparisons require repeated blocks.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(samples);
        if (!double.IsFinite(idleMilliseconds) || idleMilliseconds < 0 || idleMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(idleMilliseconds));
        if (adaptiveComparison && idleMilliseconds < 12000)
            throw new ArgumentOutOfRangeException(nameof(idleMilliseconds), "Adaptive comparisons require at least one full spare window plus checks.");
        (string Mode, int Capacity, int? Startup)[] profiles = adaptiveComparison
            ? [("fixed", 8, 2), ("adaptive", 8, 2), ("fixed", 8, 8)]
            : (from mode in new[] { "fixed", "parking" } from cap in new[] { 1, 2, 4, 8 } select (mode, cap, (int?)null)).ToArray();
        Shuffle(profiles, new Random(seed));
        List<BenchmarkCase> plan = [];
        for (var block = 0; block < blocks; block++)
        {
            var random = new Random(unchecked(seed + block * 631));
            List<BenchmarkPhase> phases = [];
            foreach (var rows in new[] { 130, 1300, 13000 })
                phases.Add(new($"rows-{rows}", 1, Enumerable.Repeat(rows, samples).ToArray()));
            foreach (var phase in Mixed)
            {
                var jobs = Enumerable.Range(0, phase.Concurrency * phase.Waves).SelectMany(_ => new[] { 130, 1300, 13000 }).ToArray();
                Shuffle(jobs, random);
                phases.Add(new(phase.Name, phase.Concurrency, jobs, phase.Concurrency == 0 ? idleMilliseconds : 0));
            }
            var order = profiles.Skip((block / 2) % profiles.Length).Concat(profiles.Take((block / 2) % profiles.Length)).ToArray();
            if (block % 2 != 0) Array.Reverse(order);
            foreach (var (mode, cap, startup) in order)
                plan.Add(new(block, mode, cap, phases.Select(phase => phase with { Rows = (int[])phase.Rows.Clone() }).ToArray(), startup));
        }
        var result = plan.ToArray();
        RequireValid(result, blocks, samples, adaptiveComparison);
        return result;
    }

    public static void RequireValid(BenchmarkCase[] cases, int blocks, int samples, bool adaptiveComparison = false)
    {
        var expectedProfiles = adaptiveComparison
            ? new[] { "adaptive-8-startup-2", "fixed-8-startup-2", "fixed-8-startup-8" }
            : new[] { "fixed-1", "fixed-2", "fixed-4", "fixed-8", "parking-1", "parking-2", "parking-4", "parking-8" };
        if (blocks < 2 || samples < 1 || cases.Length != blocks * expectedProfiles.Length)
            throw new InvalidDataException("ScheduleShape: repeated comparison cases are missing.");
        for (var block = 0; block < blocks; block++)
        {
            var peers = cases.Where(item => item.Block == block).ToArray();
            var profiles = peers.Select(item => item.Profile).Order().ToArray();
            if (!profiles.SequenceEqual(expectedProfiles))
                throw new InvalidDataException("ScheduleShape: comparison capacity/startup profiles differ.");
            foreach (var item in peers)
            {
                if (item.Phases.Length != 11) throw new InvalidDataException("ScheduleShape: required phases differ.");
                if (adaptiveComparison && item.Phases[6].IdleMilliseconds < 12000)
                    throw new InvalidDataException("ScheduleTiming: adaptive comparison idle is shorter than stabilization.");
                if (item.Phases.Any(phase => !double.IsFinite(phase.IdleMilliseconds) || phase.IdleMilliseconds < 0 ||
                        phase.IdleMilliseconds > uint.MaxValue - 1 || phase.Concurrency > 0 && phase.IdleMilliseconds != 0) ||
                    !item.Phases.Select(phase => phase.IdleMilliseconds).SequenceEqual(peers[0].Phases.Select(phase => phase.IdleMilliseconds)))
                    throw new InvalidDataException("ScheduleTiming: phase delays must be valid and equivalent across comparison profiles.");
                foreach (var rows in item.Phases.SelectMany(phase => phase.Rows))
                    if (rows is not (130 or 1300 or 13000)) throw new InvalidDataException("ScheduleWorkload: unsupported or empty load.");
                for (var i = 0; i < 3; i++)
                {
                    var expected = new[] { 130, 1300, 13000 }[i]; var phase = item.Phases[i];
                    if (phase.Name != $"rows-{expected}" || phase.Concurrency != 1 || phase.Rows.Length != samples || phase.Rows.Any(rows => rows != expected))
                        throw new InvalidDataException("ScheduleDistribution: homogeneous samples differ.");
                }
                for (var i = 0; i < Mixed.Length; i++)
                {
                    var expected = Mixed[i]; var phase = item.Phases[i + 3];
                    if (phase.Name != expected.Name || phase.Concurrency != expected.Concurrency ||
                        phase.Rows.Length != expected.Concurrency * expected.Waves * 3 ||
                        new[] { 130, 1300, 13000 }.Any(rows => phase.Rows.Count(value => value == rows) != expected.Concurrency * expected.Waves))
                        throw new InvalidDataException("ScheduleDistribution: burst samples differ.");
                }
                if (!item.Phases.SelectMany(phase => phase.Rows).SequenceEqual(peers[0].Phases.SelectMany(phase => phase.Rows)))
                    throw new InvalidDataException("ScheduleDistribution: comparison workloads differ.");
            }
        }
    }

    private static void Shuffle<T>(T[] values, Random random)
    {
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}

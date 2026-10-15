using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace TurboTest.Benchmarks;

public static class BenchmarkCommand
{
    public static async Task<int> RunAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        var blocks = 2; var samples = 2; var seed = 20261015; var idle = 2000d;
        var planOnly = false; var smoke = false; var adaptive = false; var idleSpecified = false; string? fault = null;
        var output = Path.Combine("artifacts", "pool-benchmarks", DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        BenchmarkCase[] cases;
        try
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == "--plan-only") { planOnly = true; continue; }
                if (arguments[i] == "--smoke") { smoke = true; continue; }
                if (arguments[i] == "--adaptive") { adaptive = true; continue; }
                if (i + 1 >= arguments.Length) throw new ArgumentException("Missing option value.");
                var name = arguments[i]; var value = arguments[++i];
                switch (name)
                {
                    case "--blocks": blocks = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--samples": samples = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--seed": seed = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--idle-ms": idle = double.Parse(value, CultureInfo.InvariantCulture); idleSpecified = true; break;
                    case "--output": output = value; break;
                    case "--fault": fault = value; break;
                    default: throw new ArgumentException("Unsupported option.");
                }
            }
            if (fault is not (null or "zero" or "distribution") || planOnly && smoke || adaptive && smoke) throw new ArgumentException("Invalid mode/fault.");
            if (adaptive && !idleSpecified) idle = 15000;
            cases = BenchmarkSchedule.Create(seed, blocks, samples, idle, adaptive);
            if (smoke)
                cases = new[] { "fixed", "parking" }.Select(mode => new BenchmarkCase(0, mode, 2,
                [new("first", 2, [130, 130]), new("idle", 0, [], 1), new("renewed", 2, [1300, 1300])])).ToArray();
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new ArgumentException("Output directory is not empty.");
            Directory.CreateDirectory(output);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            Console.Error.WriteLine("BenchmarkArguments: use --output PATH [--adaptive] [--blocks N>=2] [--samples N>=1] [--seed N] [--idle-ms N>=0, adaptive>=12000] [--plan-only|--smoke] [--fault zero|distribution]. Adaptive excludes smoke. Output must be empty.");
            return 2;
        }
        var modeName = planOnly ? "PlanOnly" : smoke ? "Smoke" : "Experiment";
        var comparison = adaptive ? "Adaptive" : "FixedParking";
        var policy = cases.FirstOrDefault(item => item.Mode == "adaptive")?.AdaptivePolicy;
        var json = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(output, "plan.json"), JsonSerializer.Serialize(new { Mode = modeName, Comparison = comparison, AdaptivePolicy = policy, Seed = seed, Blocks = blocks, HomogeneousSamples = samples, IdleMilliseconds = idle, Cases = cases }, json), cancellationToken);
        if (planOnly) return 0;
        var started = Stopwatch.GetTimestamp();
        List<BenchmarkCaseReport> reports = [];
        foreach (var item in cases)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Console.WriteLine($"block {item.Block + 1}, {item.Profile}");
            var directory = Path.Combine(output, $"{reports.Count:D3}-block-{item.Block}-{item.Profile}");
            reports.Add(await BenchmarkRunner.RunCaseAsync(item, directory, fault, cancellationToken));
            await WriteSummaryAsync();
        }
        await WriteSummaryAsync();
        return reports.Count == cases.Length && reports.All(report => report.Succeeded) ? 0 : 1;

        async Task WriteSummaryAsync()
        {
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var completed = reports.Sum(report => report.Samples.Count);
            var groups = reports.SelectMany(report => report.Samples.Select(sample => (report.Plan.Profile, Sample: sample)))
                .GroupBy(item => (item.Profile, item.Sample.Rows))
                .Select(group => new
                {
                    group.Key.Profile, group.Key.Rows,
                    Checkout = DurationSummary.FromMilliseconds(group.Select(item => item.Sample.CheckoutMilliseconds)),
                    Return = DurationSummary.FromMilliseconds(group.Select(item => item.Sample.ReturnMilliseconds)),
                    Cycle = DurationSummary.FromMilliseconds(group.Select(item => item.Sample.CycleMilliseconds))
                }).ToArray();
            var summary = new
            {
                Mode = modeName, Comparison = comparison, AdaptivePolicy = policy,
                Succeeded = reports.Count == cases.Length && reports.All(report => report.Succeeded),
                ExpectedCases = cases.Length, CompletedCycles = completed, TotalMilliseconds = elapsed,
                FullSuiteCyclesPerSecond = elapsed > 0 ? completed * 1000 / elapsed : 0,
                Percentiles = "p50 conventional median; p95 nearest rank ceil(.95*n); max/count; milliseconds. Failed/incomplete runs remain rejected.",
                Timing = "Checkout: public request to lease. Return: dispose request through receipt. Cycle includes baseline checks, load, validation and return. Startup/preflight/idle/monitor stop/drain are retained separately and included in total. Operation validation is a subset, not an additional cost.",
                Scope = smoke ? "Smoke boundary verification; no representative performance conclusion."
                    : adaptive ? "Local repeated fixed/adaptive comparison: equal startup2 pair and already-warm fixed8 control. Phase-specific first/renewed/repeated/heavy/idle results and actual targets remain in each case; no universal50ms guarantee."
                    : "Local repeated fixed/explicit-parking comparison; no universal50ms guarantee. Source/image/runtime/resource details and raw events reside in each case report.",
                Groups = groups,
                Cases = reports.Select(report => new
                {
                    report.Plan.Block, report.Plan.Profile, report.Succeeded, report.CleanupCompleted,
                    report.StartupState, report.PreflightState, report.AdaptivePolicy,
                    report.StartupMilliseconds, report.PreflightMilliseconds, report.MonitorStopMilliseconds, report.FinalDrainMilliseconds, report.TotalMilliseconds,
                    CompletedCycles = report.Samples.Count, report.Failures,
                    PeakProcessWorkingSetBytes = report.Resources.Length == 0 ? (long?)null : report.Resources.Max(resource => resource.ProcessWorkingSetBytes),
                    PeakContainerMemoryBytes = report.Resources.Length == 0 ? (ulong?)null : report.Resources.Max(resource => resource.ContainerMemoryBytes),
                    ResourceSamples = report.Resources.Length,
                    report.Phases
                }).ToArray()
            };
            var path = Path.Combine(output, "summary.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(summary, json));
            File.Move(path + ".tmp", path, overwrite: true);
        }
    }
}

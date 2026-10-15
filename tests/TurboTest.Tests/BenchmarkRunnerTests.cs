using System.Text.Json;
using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PostgreSQL")]
    public async Task Empty_or_idle_only_cases_are_rejected_before_fixture_startup(bool idleOnly)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-empty-benchmark-" + Guid.NewGuid().ToString("N"));
        var plan = new BenchmarkCase(0, "fixed", 1, idleOnly ? [new("idle", 0, [], 1)] : []);
        try
        {
            var report = await BenchmarkRunner.RunCaseAsync(plan, directory);
            Assert.False(report.Succeeded);
            Assert.Contains(report.Failures, failure => failure.Diagnostic == "CaseWorkload");
            Assert.Empty(report.Samples);
            Assert.Empty(report.InitialDatabases);
            Assert.Equal(0, report.StartupMilliseconds);
            Assert.Null(report.Before);
            Assert.True(report.CleanupCompleted);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "report.json")));
            Assert.False(saved.RootElement.GetProperty("Succeeded").GetBoolean());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Cancellation_after_live_leases_is_rejected_only_after_owned_cleanup_settles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-cancel-test-" + Guid.NewGuid().ToString("N"));
        using var cancel = new CancellationTokenSource();
        var plan = new BenchmarkCase(0, "fixed", 2, [new("work", 2, [130, 130])]);
        var running = BenchmarkRunner.RunCaseAsync(plan, directory, cancellationToken: cancel.Token);
        try
        {
            var progress = Path.Combine(directory, "progress.json");
            while (!File.Exists(progress) && !running.IsCompleted) await Task.Delay(10);
            Assert.True(File.Exists(progress), "No live-owner checkpoint was published.");
            using var checkpoint = JsonDocument.Parse(await File.ReadAllTextAsync(progress));
            Assert.True(checkpoint.RootElement.GetProperty("State").GetProperty("Leased").GetInt32() > 0);
            cancel.Cancel();
            var report = await running;
            Assert.False(report.Succeeded);
            Assert.True(report.CleanupCompleted);
            Assert.Equal(0, report.RemainingOwnedContainers);
            Assert.NotEmpty(report.Failures);
        }
        finally
        {
            cancel.Cancel(); await running;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("zero")]
    [InlineData("distribution")]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_runner_reports_actual_cycles_or_named_rejection_after_complete_cleanup(string? fault)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-benchmark-test-" + Guid.NewGuid().ToString("N"));
        var plan = new BenchmarkCase(0, "parking", 2,
        [
            new("first", 2, [130, 130]), new("idle", 0, [], 1), new("renewed", 2, [1300, 1300])
        ]);
        try
        {
            var report = await BenchmarkRunner.RunCaseAsync(plan, directory, fault);
            Assert.True(report.CleanupCompleted);
            Assert.Equal(2, report.InitialDatabases.Length);
            Assert.True(report.IsolationPairChecked);
            Assert.True(report.StartupMilliseconds > 0);
            Assert.True(report.FinalDrainMilliseconds > 0);
            if (fault is null)
            {
                Assert.True(report.Succeeded);
                Assert.NotNull(report.Before);
                Assert.NotNull(report.After);
                report.Before.RequireUnchanged(report.After);
                Assert.True(report.Before.Files.Length > 100);
                Assert.NotNull(report.Image);
                Assert.StartsWith("sha256:", report.Image.Id);
                Assert.True(report.Image.CachedBeforeStartup);
                Assert.NotEmpty(report.Resources);
                Assert.All(report.Resources, resource =>
                {
                    Assert.True(resource.ProcessWorkingSetBytes > 0);
                    Assert.True(resource.ContainerMemoryBytes > 0);
                    Assert.Equal(256UL * 1024 * 1024, resource.ContainerMemoryLimitBytes);
                    Assert.InRange(resource.State.Reserved, 0, 2);
                });
                Assert.Empty(report.Failures);
                Assert.Equal(4, report.Samples.Count);
                Assert.All(report.Samples, sample =>
                {
                    Assert.Contains(sample.Database, report.InitialDatabases);
                    Assert.True(sample.CheckoutMilliseconds > 0);
                    Assert.True(sample.ReturnMilliseconds > 0);
                    Assert.True(sample.CycleMilliseconds >= sample.CheckoutMilliseconds + sample.ReturnMilliseconds);
                });
                Assert.Equal(new[] { 2, 0, 2 }, report.Phases.Select(phase => phase.CompletedCycles));
                Assert.True(report.Phases[0].ThroughputPerSecond > 0);
                Assert.Equal(2, report.Budget!.WorkloadSlotLimit);
            }
            else
            {
                Assert.False(report.Succeeded);
                Assert.Empty(report.Samples);
                Assert.Contains(report.Failures, failure => failure.Diagnostic == (fault == "zero" ? "WorkloadEmpty" : "WorkloadDistribution"));
            }
            using var stored = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "report.json")));
            Assert.Equal(report.Succeeded, stored.RootElement.GetProperty("Succeeded").GetBoolean());
            Assert.True(stored.RootElement.GetProperty("CleanupCompleted").GetBoolean());
            Assert.DoesNotContain("Password=", stored.RootElement.GetRawText());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}

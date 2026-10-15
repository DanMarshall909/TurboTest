using System.Text.Json;
using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkCommandTests
{
    [Fact]
    public async Task Adaptive_plan_command_persists_matched_startup_and_default_policy_without_allocating_resources()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-adaptive-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, await BenchmarkCommand.RunAsync(["--adaptive", "--plan-only", "--output", directory]));
            using var plan = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "plan.json")));
            Assert.Equal("Adaptive", plan.RootElement.GetProperty("Comparison").GetString());
            Assert.Equal(15000, plan.RootElement.GetProperty("IdleMilliseconds").GetDouble());
            var cases = plan.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            Assert.Equal(6, cases.Length);
            Assert.Equal(new[] { 2, 2, 8 }, cases.Take(3).Select(item => item.GetProperty("Startup").GetInt32()).Order());
            var policy = plan.RootElement.GetProperty("AdaptivePolicy");
            Assert.Equal(2, policy.GetProperty("MinimumWarmDatabases").GetInt32());
            Assert.Equal(8, policy.GetProperty("MaximumWarmDatabases").GetInt32());
            Assert.Equal("00:00:01", policy.GetProperty("GrowthStabilization").GetString());
            Assert.Equal("00:00:10", policy.GetProperty("ShrinkStabilization").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "summary.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Supported_smoke_invocation_runs_both_public_modes_and_persists_cleanup_and_latency_results()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-command-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, await BenchmarkCommand.RunAsync(["--smoke", "--output", directory]));
            using var summary = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "summary.json")));
            Assert.Equal("Smoke", summary.RootElement.GetProperty("Mode").GetString());
            Assert.True(summary.RootElement.GetProperty("Succeeded").GetBoolean());
            Assert.Equal(2, summary.RootElement.GetProperty("Cases").GetArrayLength());
            Assert.Equal(8, summary.RootElement.GetProperty("CompletedCycles").GetInt32());
            Assert.Equal(2, Directory.GetFiles(directory, "report.json", SearchOption.AllDirectories).Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Supported_plan_invocation_validates_and_writes_the_exact_repeated_schedule()
    {
        var directory = Path.Combine(Path.GetTempPath(), "tt-command-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, await BenchmarkCommand.RunAsync(["--plan-only", "--output", directory, "--blocks", "2", "--samples", "2", "--seed", "17", "--idle-ms", "1234"]));
            using var plan = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "plan.json")));
            Assert.Equal("PlanOnly", plan.RootElement.GetProperty("Mode").GetString());
            var cases = plan.RootElement.GetProperty("Cases").EnumerateArray().ToArray();
            Assert.Equal(16, cases.Length);
            Assert.Equal(159, cases[0].GetProperty("Phases").EnumerateArray().Sum(phase => phase.GetProperty("Rows").GetArrayLength()));
            Assert.False(File.Exists(Path.Combine(directory, "summary.json")));
            Assert.Equal(2, await BenchmarkCommand.RunAsync(["--plan-only", "--output", directory]));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--samples", "0")]
    [InlineData("--blocks", "1")]
    [InlineData("--seed", "bad")]
    [InlineData("--adaptive", "--idle-ms", "11999")]
    [InlineData("--adaptive", "--smoke")]
    public async Task Unsupported_or_invalid_cli_cannot_start_an_experiment(params string[] arguments)
    {
        Assert.Equal(2, await BenchmarkCommand.RunAsync(arguments));
    }
}

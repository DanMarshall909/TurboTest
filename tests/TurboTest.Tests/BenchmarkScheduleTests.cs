using TurboTest.Benchmarks;

namespace TurboTest.Tests;

public class BenchmarkScheduleTests
{
    [Fact]
    public void Adaptive_comparison_pairs_equal_startup_with_an_already_warm_fixed_control()
    {
        var plan = BenchmarkSchedule.Create(17, 2, 2, 15000, adaptiveComparison: true);
        Assert.Equal(6, plan.Length);
        var first = plan.Take(3).ToArray();
        Assert.Equal(new[] { "adaptive-8-startup-2", "fixed-8-startup-2", "fixed-8-startup-8" }, first.Select(item => item.Profile).Order());
        Assert.Equal(first.Select(item => item.Profile).Reverse(), plan.Skip(3).Select(item => item.Profile));
        Assert.All(plan, item => Assert.Equal(8, item.Capacity));
        Assert.Equal(new[] { 2, 2, 8 }, first.Select(item => item.Startup).Order());
        Assert.Equal(954, plan.Sum(item => item.Phases.Sum(phase => phase.Rows.Length)));
        foreach (var block in plan.GroupBy(item => item.Block))
        {
            var peer = block.First();
            foreach (var item in block)
            {
                Assert.Equal(11, item.Phases.Length);
                Assert.Equal(peer.Phases.SelectMany(phase => phase.Rows), item.Phases.SelectMany(phase => phase.Rows));
                Assert.Equal(15000, item.Phases.Single(phase => phase.Name == "idle").IdleMilliseconds);
            }
        }
        BenchmarkSchedule.RequireValid(plan, 2, 2, adaptiveComparison: true);
    }

    [Fact]
    public void Adaptive_comparison_requires_a_real_stabilization_idle_and_preserves_matched_startup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkSchedule.Create(17, 2, 2, 11999, adaptiveComparison: true));
        var plan = BenchmarkSchedule.Create(17, 2, 2, 15000, adaptiveComparison: true);
        plan[0] = plan[0] with { StartupWarmDatabases = 1 };
        var failure = Assert.Throws<InvalidDataException>(() => BenchmarkSchedule.RequireValid(plan, 2, 2, adaptiveComparison: true));
        Assert.Contains("ScheduleShape", failure.Message);
        plan = BenchmarkSchedule.Create(17, 2, 2, 15000, adaptiveComparison: true);
        plan[0].Phases[6] = plan[0].Phases[6] with { IdleMilliseconds = 11999 };
        var timing = Assert.Throws<InvalidDataException>(() => BenchmarkSchedule.RequireValid(plan, 2, 2, adaptiveComparison: true));
        Assert.Contains("ScheduleTiming", timing.Message);
    }

    [Fact]
    public void Repeated_configs_have_counterbalanced_order_and_equivalent_real_workloads()
    {
        var plan = BenchmarkSchedule.Create(17, 2, 2, 1234);
        Assert.Equal(16, plan.Length);
        var first = plan.Where(item => item.Block == 0).ToArray();
        var second = plan.Where(item => item.Block == 1).ToArray();
        Assert.Equal(new[] { "fixed-1", "fixed-2", "fixed-4", "fixed-8", "parking-1", "parking-2", "parking-4", "parking-8" }, first.Select(item => item.Profile).Order());
        Assert.Equal(first.Select(item => item.Profile).Reverse(), second.Select(item => item.Profile));
        Assert.Equal(plan.Select(item => item.Profile), BenchmarkSchedule.Create(17, 2, 2, 1234).Select(item => item.Profile));
        Assert.NotEqual(first.Select(item => item.Profile), BenchmarkSchedule.Create(31, 2, 2, 1234).Take(8).Select(item => item.Profile));
        foreach (var item in plan)
        {
            var phases = item.Phases;
            Assert.Equal(new[] { "rows-130", "rows-1300", "rows-13000", "single-start", "first-burst", "single-after", "idle", "renewed-burst", "repeat-burst", "sustained-heavy", "single-final" }, phases.Select(phase => phase.Name));
            Assert.Equal(new[] { 1, 1, 1, 1, 8, 1, 0, 8, 8, 8, 1 }, phases.Select(phase => phase.Concurrency));
            Assert.Equal(1234, phases[6].IdleMilliseconds);
            Assert.Empty(phases[6].Rows);
            Assert.All(phases.Take(3), phase => Assert.Equal(2, phase.Rows.Length));
            var timed = phases.SelectMany(phase => phase.Rows).ToArray();
            Assert.Equal(159, timed.Length);
            Assert.Equal(53, timed.Count(rows => rows == 130));
            Assert.Equal(53, timed.Count(rows => rows == 1300));
            Assert.Equal(53, timed.Count(rows => rows == 13000));
            var peer = plan.First(peer => peer.Block == item.Block);
            Assert.Equal(peer.Phases.SelectMany(phase => phase.Rows), timed);
        }
        BenchmarkSchedule.RequireValid(plan, 2, 2);
    }

    [Fact]
    public void A_zero_or_misdistributed_plan_is_rejected_before_any_fixture_starts()
    {
        var plan = BenchmarkSchedule.Create(17, 2, 2, 1);
        Assert.NotEmpty(plan);
        plan[0].Phases[0].Rows[0] = 0;
        var zero = Assert.Throws<InvalidDataException>(() => BenchmarkSchedule.RequireValid(plan, 2, 2));
        Assert.Contains("ScheduleWorkload", zero.Message);
        plan = BenchmarkSchedule.Create(17, 2, 2, 1);
        plan[0].Phases[0].Rows[0] = 1300;
        var mix = Assert.Throws<InvalidDataException>(() => BenchmarkSchedule.RequireValid(plan, 2, 2));
        Assert.Contains("ScheduleDistribution", mix.Message);
    }

    [Theory]
    [InlineData(6, 2)]
    [InlineData(6, -1)]
    [InlineData(6, double.NaN)]
    [InlineData(6, double.PositiveInfinity)]
    [InlineData(6, (double)uint.MaxValue)]
    [InlineData(0, 1)]
    public void Invalid_or_unequal_phase_delays_are_rejected_before_any_fixture_starts(int phaseIndex, double delay)
    {
        var plan = BenchmarkSchedule.Create(17, 2, 2, 1);
        plan[0].Phases[phaseIndex] = plan[0].Phases[phaseIndex] with { IdleMilliseconds = delay };
        var failure = Assert.Throws<InvalidDataException>(() => BenchmarkSchedule.RequireValid(plan, 2, 2));
        Assert.Contains("ScheduleTiming", failure.Message);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(2, 2, -1)]
    public void Non_repeated_or_invalid_experiments_are_not_accepted(int blocks, int samples, double idle)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkSchedule.Create(17, blocks, samples, idle));
    }
}

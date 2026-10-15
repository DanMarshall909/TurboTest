using Docker.DotNet.Models;
using Docker.DotNet;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

namespace TurboTest.Tests;

public class HostMemoryTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Real_one_shot_host_statistics_calibrate_cache_conversion_and_the_owned_adapter()
    {
        await using var host = new TtPool(new TtOptions { PoolSize = 1, ContainerPrefix = "tt_memory_" + Guid.NewGuid().ToString("N") },
            administratorHost: true);
        await host.InitializeAsync();
        var id = Assert.Single(host.OwnedResourceIds);
        using var docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
        var capture = new Capture();
        await docker.Containers.GetContainerStatsAsync(id, new ContainerStatsParameters { Stream = false, OneShot = true }, capture);
        Assert.Equal(1, capture.Count);
        var stats = Assert.IsType<ContainerStatsResponse>(capture.Value);
        Assert.Equal(id, stats.ID);
        var values = stats.MemoryStats.Stats;
        var cache = values.TryGetValue("total_inactive_file", out var v1) ? v1 : values["inactive_file"];
        Assert.True(stats.MemoryStats.Usage >= cache);
        var converted = TtHostMemory.Read(stats, 37);
        Assert.NotNull(converted);
        Assert.Equal((long)(stats.MemoryStats.Usage - cache), converted.WorkingSetBytes);
        Assert.Equal((long)stats.MemoryStats.Limit, converted.LimitBytes);
        Assert.Equal(37, converted.ObservedAt);
        var clock = new ManualPoolClock();
        clock.Advance(TimeSpan.FromSeconds(13));
        var reading = await host.ReadOwnedHostMemoryAsync(clock, default);
        Assert.NotNull(reading);
        Assert.InRange(reading.WorkingSetBytes, 1, 256L * 1024 * 1024);
        Assert.Equal(256L * 1024 * 1024, reading.LimitBytes);
        Assert.Equal(TimeSpan.FromSeconds(13).Ticks, reading.ObservedAt);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.ReadOwnedHostMemoryAsync(clock, cancellation.Token));
    }

    private sealed class Capture : IProgress<ContainerStatsResponse>
    {
        internal ContainerStatsResponse? Value { get; private set; }
        internal int Count { get; private set; }
        public void Report(ContainerStatsResponse value) { Value = value; Count++; }
    }

    [Fact]
    public void Cgroup_v1_cache_takes_precedence_and_v2_uses_its_own_inactive_file_key()
    {
        var v1 = TtHostMemory.Read(new ContainerStatsResponse
        {
            MemoryStats = new MemoryStats { Usage = 500, Limit = 1000,
                Stats = new Dictionary<string, ulong> { ["total_inactive_file"] = 400, ["inactive_file"] = 100 } }
        }, 37);
        Assert.NotNull(v1);
        Assert.Equal(new TtMemoryReading(100, 1000, 37), v1);
        var v2 = TtHostMemory.Read(new ContainerStatsResponse
        {
            MemoryStats = new MemoryStats { Usage = 500, Limit = 1000,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 350 } }
        }, 41);
        Assert.Equal(new TtMemoryReading(150, 1000, 41), v2);
    }

    [Fact]
    public void Zero_cache_is_valid_but_missing_or_inconsistent_measurements_are_unavailable()
    {
        var valid = new ContainerStatsResponse
        {
            MemoryStats = new MemoryStats { Usage = 500, Limit = 1000,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 0 } }
        };
        Assert.Equal(new TtMemoryReading(500, 1000, 19), TtHostMemory.Read(valid, 19));
        Assert.Null(TtHostMemory.Read(new ContainerStatsResponse(), 19));
        foreach (var memory in new[]
        {
            new MemoryStats { Usage = 500, Limit = 1000 },
            new MemoryStats { Usage = 500, Limit = 1000, Stats = new Dictionary<string, ulong> { ["cache"] = 100 } },
            new MemoryStats { Usage = 500, Limit = 0, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 100 } },
            new MemoryStats { Usage = 0, Limit = 1000, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 0 } },
            new MemoryStats { Usage = 500, Limit = 1000, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 501 } },
            new MemoryStats { Usage = 500, Limit = 399, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 100 } },
            new MemoryStats { Usage = ulong.MaxValue, Limit = ulong.MaxValue, Stats = new Dictionary<string, ulong> { ["inactive_file"] = 100 } }
        }) Assert.Null(TtHostMemory.Read(new ContainerStatsResponse { MemoryStats = memory }, 19));
    }
}

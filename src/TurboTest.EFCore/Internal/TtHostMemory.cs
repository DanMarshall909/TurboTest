using Docker.DotNet.Models;

namespace TurboTest.EFCore.Internal;

internal static class TtHostMemory
{
    internal static TtMemoryReading? Read(ContainerStatsResponse stats, long observedAt)
    {
        if (stats.MemoryStats is not { } memory || memory.Usage == 0 || memory.Limit == 0 ||
            memory.Usage > long.MaxValue || memory.Limit > long.MaxValue || memory.Stats is not { } values)
            return null;
        // Docker CLI semantics: v1 total_inactive_file takes precedence over v2 inactive_file.
        // https://docs.docker.com/reference/cli/docker/container/stats/
        if (!values.TryGetValue("total_inactive_file", out var cache) && !values.TryGetValue("inactive_file", out cache))
            return null;
        if (cache > memory.Usage) return null;
        var working = memory.Usage - cache;
        if (working > memory.Limit) return null;
        return new((long)working, (long)memory.Limit, observedAt);
    }
}

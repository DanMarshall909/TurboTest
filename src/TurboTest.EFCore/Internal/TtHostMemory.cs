using Docker.DotNet.Models;

namespace TurboTest.EFCore.Internal;

internal static class TtHostMemory
{
    internal static bool IsForRequestedHost(ContainerStatsResponse stats, string requestedHostId)
        // The full immutable ID already scopes the stats request. Docker 28's
        // one-shot path omits ID; an explicitly different ID is still invalid.
        => string.IsNullOrEmpty(stats.ID) || stats.ID == requestedHostId;

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

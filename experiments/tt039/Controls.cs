using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TurboTest.Benchmarks;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

internal static class Controls
{
    internal static int ProbeCost(string output)
    {
        var measurements = new List<object>();
        foreach (var enabled in new[] { false, true })
            for (var round = 0; round < 3; round++)
            {
                TraceProbe.Enabled = enabled;
                TraceProbe.Events.Clear();
                var began = Stopwatch.GetTimestamp();
                for (var i = 0; i < 50_000; i++) TraceProbe.Emit("probe");
                var elapsed = Stopwatch.GetElapsedTime(began).TotalMilliseconds;
                measurements.Add(new { Enabled = enabled, Round = round, Count = 50_000,
                    MicrosecondsPerEvent = elapsed * 1000 / 50_000 });
            }
        TraceProbe.Events.Clear();
        File.WriteAllText(output, JsonSerializer.Serialize(measurements));
        return 0;
    }

    internal static async Task<int> HeldReset(string mode, string output)
    {
        TraceProbe.AwaitReturn = mode == "awaited";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resets = 0;
        var options = new TtOptions
        {
            PoolSize = 2, StartupWarmDatabases = 2, WarmFloor = 2,
            ResetConcurrency = 1, PreparationConcurrency = 1, MaxAdministratorConnections = 4,
            MaxConnectionsPerDatabase = 4, MemoryLimitMb = 1024, MaxContainers = 1,
            DockerHost = "unix:///var/run/docker.sock", DockerImage = "postgres:16-alpine",
            ContainerPrefix = "tt039r2_control_" + Guid.NewGuid().ToString("N")
        };
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { Prefix = options.ContainerPrefix }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var fixture = await Tt.CreatePreparedFactoryAsync(options, BenchmarkWorkload.Baseline, deadline.Token,
            afterRowCommit: async _ =>
            {
                if (Interlocked.Increment(ref resets) == 1) { entered.SetResult(); await release.Task; }
            });
        try
        {
            TraceProbe.Request.Value = "control/0";
            var first = await fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
            var dirty = first.Context.Database.GetDbConnection().Database;
            await first.Context.Notes.ExecuteDeleteAsync(deadline.Token);
            var returning = first.DisposeAsync().AsTask();
            await entered.Task.WaitAsync(deadline.Token);
            if (TraceProbe.AwaitReturn == returning.IsCompleted)
                throw new InvalidDataException("ReceiptBoundary: wrong held-reset return behavior");
            TraceProbe.Request.Value = "control/1";
            var second = await fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
            if (second.Context.Database.GetDbConnection().Database == dirty)
                throw new InvalidDataException("DirtyReuse: held reset was admitted");
            await BenchmarkWorkload.VerifyBaselineAsync(second.Context);
            TraceProbe.Request.Value = "control/2";
            var waiting = fixture.CreateLeaseAsync(cancellationToken: deadline.Token);
            if (waiting.IsCompleted) throw new InvalidDataException("Capacity: no clean slot should remain");
            var shutdown = fixture.DisposeAsync().AsTask();
            await Task.Delay(30, deadline.Token);
            if (shutdown.IsCompleted) throw new InvalidDataException("Drain: shutdown abandoned held reset");
            release.SetResult();
            await returning.WaitAsync(deadline.Token);
            try { await waiting; throw new InvalidDataException("Admission: shutdown admitted queued demand"); }
            catch (ObjectDisposedException) { }
            await shutdown.WaitAsync(deadline.Token);
            if (fixture.OwnedResourceIds.Count != 0 || fixture.PoolState.Reserved != 0 || TraceProbe.ActiveReturns != 0)
                throw new InvalidDataException("Drain: resources or wrappers remain");
            await File.WriteAllTextAsync(Path.Combine(output, "control.json"), JsonSerializer.Serialize(new
            {
                Passed = true, Mode = mode, HeldReceipt = TraceProbe.AwaitReturn,
                CleanAlternate = true, ExhaustedAcquisitionWaited = true, ShutdownJoinedReset = true,
                Boundaries = TraceProbe.Events, Frequency = Stopwatch.Frequency
            }));
            return 0;
        }
        finally
        {
            release.TrySetResult();
            await fixture.DisposeAsync();
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TurboTest.EFCore;

namespace TurboTest.Benchmarks;

public sealed record DatabaseIdentity(string Name, uint Oid);
public sealed record BenchmarkSample(int Ordinal, string Phase, int Rows, DatabaseIdentity Database,
    double CheckoutMilliseconds, double ReturnMilliseconds, double CycleMilliseconds);
public sealed record BenchmarkFailure(string Category, string Diagnostic, string ExceptionType);
public sealed record PhaseReport(string Name, int Concurrency, double WallMilliseconds, int CompletedCycles,
    double ThroughputPerSecond, DurationSummary Checkout, DurationSummary Return, DurationSummary Cycle)
{
    public TtPoolSnapshot? StartState { get; init; }
    public TtPoolSnapshot? EndState { get; init; }
}
public sealed record BenchmarkEvent(double ElapsedMilliseconds, string Phase, int EventId, string? Name,
    Dictionary<string, object?> Fields);
public sealed record ResourceSample(double ElapsedMilliseconds, string Phase, long ProcessWorkingSetBytes,
    ulong ContainerMemoryBytes, ulong ContainerMemoryLimitBytes, TtPoolSnapshot State);
public sealed record ImageIdentity(string Requested, string Id, string[] RepositoryDigests, bool CachedBeforeStartup);

public sealed class BenchmarkCaseReport
{
    public required BenchmarkCase Plan { get; init; }
    public bool Succeeded { get; set; }
    public bool CleanupCompleted { get; set; }
    public bool IsolationPairChecked { get; set; }
    public double StartupMilliseconds { get; set; }
    public double PreflightMilliseconds { get; set; }
    public double FinalDrainMilliseconds { get; set; }
    public double TotalMilliseconds { get; set; }
    public double MonitorStopMilliseconds { get; set; }
    public TtResourceBudget? Budget { get; set; }
    public DatabaseIdentity[] InitialDatabases { get; set; } = [];
    public DatabaseIdentity[] KnownDatabases { get; set; } = [];
    public TtPoolSnapshot? StartupState { get; set; }
    public TtPoolSnapshot? PreflightState { get; set; }
    public TtAdaptiveOptions? AdaptivePolicy { get; set; }
    public List<BenchmarkSample> Samples { get; } = [];
    public List<BenchmarkFailure> Failures { get; } = [];
    public List<PhaseReport> Phases { get; } = [];
    public BenchmarkEvent[] Events { get; set; } = [];
    public int RemainingOwnedContainers { get; set; }
    public string? PostgreSqlVersion { get; set; }
    public SourceBinding? Before { get; set; }
    public SourceBinding? After { get; set; }
    public ImageIdentity? Image { get; set; }
    public ResourceSample[] Resources { get; set; } = [];
    public string ResourceDefinition { get; } = "Sampled .NET working set and Docker cgroup memory usage including cache; peaks are sampled lower bounds. Workload/idle sampling ends before final host deletion; startup and drain wall costs are reported separately.";
}

public static class BenchmarkRunner
{
    public static async Task<BenchmarkCaseReport> RunCaseAsync(BenchmarkCase plan, string outputDirectory,
        string? fault = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var report = new BenchmarkCaseReport { Plan = plan };
        var identities = new BenchmarkDatabaseRegistry(plan.Capacity);
        var total = Stopwatch.GetTimestamp();
        var logger = new EventLogger(total);
        var failures = new ConcurrentQueue<BenchmarkFailure>();
        var resources = new ConcurrentQueue<ResourceSample>();
        using var stopMonitor = new CancellationTokenSource();
        Task? monitor = null;
        var prefix = "ttbench_" + Guid.NewGuid().ToString("N");
        var dockerHost = OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
        using var docker = new DockerClientConfiguration(new Uri(dockerHost)).CreateClient();
        TtFactory<BenchmarkContext>? fixture = null;
        var stage = "Configuration";
        try
        {
            if (plan.Mode is not ("fixed" or "parking" or "adaptive") || plan.Capacity is not (1 or 2 or 4 or 8) ||
                plan.Startup < 1 || plan.Startup > plan.Capacity ||
                plan.Phases.Any(phase => phase.Concurrency < 0 || phase.Concurrency > 8 ||
                    phase.Concurrency == 0 && phase.Rows.Length != 0 || phase.Concurrency > 0 && phase.Rows.Length == 0 ||
                    !double.IsFinite(phase.IdleMilliseconds) || phase.IdleMilliseconds < 0))
                throw new InvalidDataException("CaseConfiguration: invalid mode, capacity or phase.");
            if (!plan.Phases.Any(phase => phase.Rows.Length > 0))
                throw new InvalidDataException("CaseWorkload: a case requires timed workload cycles.");
            foreach (var rows in plan.Phases.SelectMany(phase => phase.Rows)) _ = WorkloadMix.ForTotal(rows);
            if (fault is not (null or "zero" or "distribution")) throw new InvalidDataException("CaseConfiguration: unsupported calibration fault.");
            stage = "Binding";
            var repository = RepositoryRoot();
            report.Before = await SourceBinding.CaptureAsync(repository, Path.Combine(outputDirectory, "before"), cancellationToken);
            stage = "ImageMetadata";
            var image = await docker.Images.InspectImageAsync("postgres:16-alpine", cancellationToken);
            report.Image = new("postgres:16-alpine", image.ID, image.RepoDigests?.ToArray() ?? [], true);
            stage = "Startup";
            var startup = Stopwatch.GetTimestamp();
            report.AdaptivePolicy = plan.AdaptivePolicy;
            try
            {
                fixture = await Tt.CreateFactoryAsync(BenchmarkWorkload.Baseline, options =>
                {
                    options.PoolSize = plan.Capacity; options.WarmFloor = 0;
                    options.StartupWarmDatabases = plan.Startup;
                    options.EnableDynamicScaling = plan.Mode == "adaptive";
                    options.AdaptivePool = report.AdaptivePolicy ?? new();
                    options.MaxParkedDatabases = plan.Capacity;
                    options.PreparationConcurrency = 1; options.ResetConcurrency = 1;
                    options.MaxConnectionsPerDatabase = 4; options.MaxAdministratorConnections = 2;
                    options.MemoryLimitMb = 256; options.MaxContainers = 1;
                    options.DockerImage = "postgres:16-alpine"; options.DockerHost = dockerHost;
                    options.ContainerPrefix = prefix; options.Logger = logger; options.LogLevel = LogLevel.Debug;
                    options.StartupTimeout = TimeSpan.FromMinutes(2); options.DrainTimeout = TimeSpan.FromMinutes(2);
                }, cancellationToken);
            }
            finally { report.StartupMilliseconds = Stopwatch.GetElapsedTime(startup).TotalMilliseconds; }
            report.Budget = fixture.ResourceBudget;
            report.StartupState = fixture.PoolState;
            stage = "Resources";
            var owned = await OwnedContainersAsync(docker, prefix, cancellationToken);
            if (owned.Length != 1 || owned[0].ImageID != image.ID)
                throw new InvalidDataException("ImageIdentity: owned host does not match the captured cached image.");
            resources.Enqueue(await CaptureResourceAsync(docker, owned[0].ID, total, logger.Phase, fixture.PoolState, cancellationToken));
            monitor = MonitorAsync(docker, owned[0].ID, fixture, total, logger, resources, failures, stopMonitor.Token);
            stage = logger.Phase = "Preflight";
            var preflight = Stopwatch.GetTimestamp();
            try
            {
                var leases = await Task.WhenAll(Enumerable.Range(0, plan.Startup).Select(_ => fixture.CreateLeaseAsync(cancellationToken: cancellationToken)));
                try
                {
                    report.InitialDatabases = await Task.WhenAll(leases.Select(lease => ReadIdentityAsync(lease.Context, cancellationToken)));
                    if (report.InitialDatabases.Distinct().Count() != plan.Startup)
                        throw new InvalidDataException("DatabaseIdentity: simultaneously leased databases are not distinct.");
                    foreach (var identity in report.InitialDatabases) identities.Observe(identity);
                    var checkpoint = Path.Combine(outputDirectory, "progress.json");
                    await File.WriteAllTextAsync(checkpoint + ".tmp", JsonSerializer.Serialize(new
                    {
                        Phase = "PreflightLeases", ElapsedMilliseconds = Stopwatch.GetElapsedTime(total).TotalMilliseconds,
                        State = fixture.PoolState, Databases = report.InitialDatabases
                    }), cancellationToken);
                    File.Move(checkpoint + ".tmp", checkpoint, overwrite: true);
                    report.PostgreSqlVersion = await leases[0].Context.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").SingleAsync(cancellationToken);
                    if (leases.Length > 1)
                    {
                        var one = "isolation-a-" + Guid.NewGuid().ToString("N"); var two = "isolation-b-" + Guid.NewGuid().ToString("N");
                        await BenchmarkWorkload.LoadAsync(leases[0].Context, WorkloadMix.ForTotal(130), one, cancellationToken);
                        await BenchmarkWorkload.LoadAsync(leases[1].Context, WorkloadMix.ForTotal(130), two, cancellationToken);
                        await BenchmarkWorkload.ValidateMutatedAsync(leases[0].Context, WorkloadMix.ForTotal(130), one, cancellationToken);
                        await BenchmarkWorkload.ValidateMutatedAsync(leases[1].Context, WorkloadMix.ForTotal(130), two, cancellationToken);
                        if (await HasMarkerAsync(leases[0].Context, two, cancellationToken) || await HasMarkerAsync(leases[1].Context, one, cancellationToken))
                            throw new InvalidDataException("CrossDatabaseMarker: a marker crossed owned databases.");
                        report.IsolationPairChecked = true;
                    }
                }
                finally { await Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask())); }
                foreach (var rows in new[] { 130, 1300, 13000 })
                {
                    await using var lease = await fixture.CreateLeaseAsync(cancellationToken: cancellationToken);
                    await BenchmarkWorkload.VerifyBaselineAsync(lease.Context, cancellationToken);
                    var marker = "preflight-" + Guid.NewGuid().ToString("N");
                    await BenchmarkWorkload.LoadAsync(lease.Context, WorkloadMix.ForTotal(rows), marker, cancellationToken);
                    await BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(rows), marker, cancellationToken);
                }
            }
            finally { report.PreflightMilliseconds = Stopwatch.GetElapsedTime(preflight).TotalMilliseconds; }
            report.PreflightState = fixture.PoolState;
            var ordinal = 0;
            foreach (var phase in plan.Phases)
            {
                stage = logger.Phase = phase.Name;
                var phaseState = fixture.PoolState;
                var phaseStarted = Stopwatch.GetTimestamp();
                var samples = new ConcurrentQueue<BenchmarkSample>();
                if (phase.Concurrency == 0)
                {
                    if (plan.Mode == "parking") await fixture.MaintainIdleAsync(cancellationToken);
                    await Task.Delay(TimeSpan.FromMilliseconds(phase.IdleMilliseconds), cancellationToken);
                }
                else
                {
                    var offset = ordinal; ordinal += phase.Rows.Length;
                    await Task.WhenAll(Enumerable.Range(0, phase.Concurrency).Select(async worker =>
                    {
                        for (var index = worker; index < phase.Rows.Length; index += phase.Concurrency)
                        {
                            TtLease<BenchmarkContext>? lease = null;
                            var returned = false;
                            var category = "Checkout";
                            var cycle = Stopwatch.GetTimestamp();
                            try
                            {
                                lease = await fixture.CreateLeaseAsync(cancellationToken: cancellationToken);
                                var checkout = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds;
                                category = "Workload";
                                var identity = await ReadIdentityAsync(lease.Context, cancellationToken);
                                await BenchmarkWorkload.VerifyBaselineAsync(lease.Context, cancellationToken);
                                identities.Observe(identity);
                                var mix = WorkloadMix.ForTotal(phase.Rows[index]);
                                var marker = "load-" + Guid.NewGuid().ToString("N");
                                if (fault != "zero") await BenchmarkWorkload.LoadAsync(lease.Context, mix, marker, cancellationToken);
                                if (fault == "distribution") await lease.Context.Notes.ExecuteDeleteAsync(cancellationToken);
                                await BenchmarkWorkload.ValidateMutatedAsync(lease.Context, mix, marker, cancellationToken);
                                category = "Return";
                                var returning = Stopwatch.GetTimestamp(); returned = true;
                                await lease.DisposeAsync();
                                var returnDuration = Stopwatch.GetElapsedTime(returning).TotalMilliseconds;
                                samples.Enqueue(new(offset + index, phase.Name, mix.Total, identity, checkout, returnDuration, Stopwatch.GetElapsedTime(cycle).TotalMilliseconds));
                            }
                            catch (Exception error) { failures.Enqueue(Failure(category, error)); break; }
                            finally
                            {
                                if (lease is not null && !returned)
                                    try { await lease.DisposeAsync(); }
                                    catch (Exception error) { failures.Enqueue(Failure("Return", error)); }
                            }
                        }
                    }));
                }
                var completed = samples.OrderBy(sample => sample.Ordinal).ToArray();
                var elapsed = Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds;
                report.Samples.AddRange(completed);
                report.Phases.Add(new(phase.Name, phase.Concurrency, elapsed, completed.Length,
                    elapsed > 0 ? completed.Length * 1000 / elapsed : 0,
                    DurationSummary.FromMilliseconds(completed.Select(sample => sample.CheckoutMilliseconds)),
                    DurationSummary.FromMilliseconds(completed.Select(sample => sample.ReturnMilliseconds)),
                    DurationSummary.FromMilliseconds(completed.Select(sample => sample.CycleMilliseconds)))
                    { StartState = phaseState, EndState = fixture.PoolState });
                if (!failures.IsEmpty) break;
            }
            stage = logger.Phase = "FinalValidation";
            await using (var final = await fixture.CreateLeaseAsync(cancellationToken: cancellationToken))
                await BenchmarkWorkload.VerifyBaselineAsync(final.Context, cancellationToken);
        }
        catch (Exception error)
        {
            failures.Enqueue(Failure(stage, error));
            if (error is TtLifecycleException { Cleanup: { } cleanup }) await RetryCleanupAsync(cleanup, failures);
        }
        finally
        {
            logger.Phase = "FinalDrain";
            var stoppingMonitor = Stopwatch.GetTimestamp();
            await stopMonitor.CancelAsync();
            if (monitor is not null) await monitor;
            report.MonitorStopMilliseconds = Stopwatch.GetElapsedTime(stoppingMonitor).TotalMilliseconds;
            var drain = Stopwatch.GetTimestamp();
            if (fixture is not null)
            {
                try { await fixture.DisposeAsync(); }
                catch (Exception error)
                {
                    failures.Enqueue(Failure("FinalDrain", error));
                    if (error is TtLifecycleException { Cleanup: { } cleanup }) await RetryCleanupAsync(cleanup, failures);
                }
            }
            report.FinalDrainMilliseconds = Stopwatch.GetElapsedTime(drain).TotalMilliseconds;
            try
            {
                report.RemainingOwnedContainers = (await OwnedContainersAsync(docker, prefix, CancellationToken.None)).Length;
                report.CleanupCompleted = report.RemainingOwnedContainers == 0 && (fixture is null || fixture.PoolState.Reserved == 0);
                if (!report.CleanupCompleted) failures.Enqueue(new("FinalDrain", "CleanupIncomplete", "OwnedResource"));
            }
            catch (Exception error) { failures.Enqueue(Failure("CleanupObservation", error)); }
            if (report.Before is not null)
                try
                {
                    report.After = await SourceBinding.CaptureAsync(RepositoryRoot(), Path.Combine(outputDirectory, "after"));
                    report.Before.RequireUnchanged(report.After);
                    var image = await docker.Images.InspectImageAsync("postgres:16-alpine");
                    if (image.ID != report.Image?.Id) throw new InvalidDataException("ImageChanged: image identity changed during the experiment.");
                }
                catch (Exception error) { failures.Enqueue(Failure("Binding", error)); }
            report.Resources = resources.OrderBy(resource => resource.ElapsedMilliseconds).ToArray();
            report.Events = logger.Events.OrderBy(record => record.ElapsedMilliseconds).ToArray();
            report.KnownDatabases = identities.Snapshot;
            report.Failures.AddRange(failures);
            report.Succeeded = failures.IsEmpty && report.CleanupCompleted;
            report.TotalMilliseconds = Stopwatch.GetElapsedTime(total).TotalMilliseconds;
            await File.WriteAllTextAsync(Path.Combine(outputDirectory, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report;
    }

    private static async Task<ContainerListResponse[]> OwnedContainersAsync(DockerClient docker, string prefix, CancellationToken token)
    {
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, token);
        return containers.Where(container => container.Names.Any(name => name.TrimStart('/').StartsWith(prefix + "_pool_", StringComparison.Ordinal))).ToArray();
    }

    private static async Task<ResourceSample> CaptureResourceAsync(DockerClient docker, string container, long started,
        string phase, TtPoolSnapshot state, CancellationToken token)
    {
        var result = new TaskCompletionSource<ContainerStatsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        await docker.Containers.GetContainerStatsAsync(container, new ContainerStatsParameters { Stream = false },
            new DirectProgress<ContainerStatsResponse>(stats => result.TrySetResult(stats)), token);
        var stats = await result.Task.WaitAsync(token);
        if (stats.MemoryStats is null || stats.MemoryStats.Usage == 0 || stats.MemoryStats.Limit == 0)
            throw new InvalidDataException("ResourceMissing: container memory reading is unavailable.");
        using var process = Process.GetCurrentProcess();
        return new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, phase, process.WorkingSet64,
            stats.MemoryStats.Usage, stats.MemoryStats.Limit, state);
    }

    private static async Task MonitorAsync(DockerClient docker, string container, TtFactory<BenchmarkContext> fixture, long started,
        EventLogger logger, ConcurrentQueue<ResourceSample> resources, ConcurrentQueue<BenchmarkFailure> failures, CancellationToken token)
    {
        try
        {
            while (true)
            {
                await Task.Delay(250, token);
                resources.Enqueue(await CaptureResourceAsync(docker, container, started, logger.Phase, fixture.PoolState, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { failures.Enqueue(Failure("ResourceMonitor", error)); }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TurboTest.sln"))) return directory.FullName;
        throw new InvalidDataException("SourceBindingMissing: repository root could not be found.");
    }

    private sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static async Task<DatabaseIdentity> ReadIdentityAsync(BenchmarkContext context, CancellationToken token)
    {
        await context.Database.OpenConnectionAsync(token);
        await using var command = new NpgsqlCommand("SELECT current_database(),oid FROM pg_database WHERE datname=current_database()", (NpgsqlConnection)context.Database.GetDbConnection());
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidDataException("DatabaseIdentity: database identity is unavailable.");
        return new(reader.GetString(0), reader.GetFieldValue<uint>(1));
    }

    private static async Task<bool> HasMarkerAsync(BenchmarkContext context, string marker, CancellationToken token) =>
        await context.Parents.AnyAsync(row => row.Name.StartsWith(marker), token) ||
        await context.Children.AnyAsync(row => row.Name.StartsWith(marker), token) ||
        await context.Notes.AnyAsync(row => row.Body.StartsWith(marker), token);

    private static BenchmarkFailure Failure(string category, Exception error) => new(category,
        error is InvalidDataException ? error.Message.Split(':', 2)[0] : error is TtLifecycleException lifecycle ? lifecycle.Stage : error.GetType().Name,
        error.GetType().Name);

    private static async Task RetryCleanupAsync(TtCleanup cleanup, ConcurrentQueue<BenchmarkFailure> failures)
    {
        for (var attempt = 0; attempt < 3; attempt++)
            try { await cleanup.RetryCleanupAsync(); return; }
            catch (Exception error) { failures.Enqueue(Failure("DeferredCleanup", error)); }
    }

    private sealed class EventLogger(long started) : ILogger
    {
        internal string Phase { get; set; } = "Startup";
        internal ConcurrentQueue<BenchmarkEvent> Events { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id is not (400 or 401 or 402 or 403 or 404)) return;
            var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).Where(pair => pair.Key != "{OriginalFormat}").ToDictionary();
            Events.Enqueue(new(Stopwatch.GetElapsedTime(started).TotalMilliseconds, Phase, eventId.Id, eventId.Name, fields));
        }
    }
}

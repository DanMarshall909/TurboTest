using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TurboTest.Benchmarks;
using TurboTest.EFCore;
using TurboTest.EFCore.Internal;

if (args[0] == "held-control") return await Controls.HeldReset(args[1], args[2]);
if (args[0] == "probe-cost") return Controls.ProbeCost(args[1]);

var output = Path.GetFullPath(args[0]);
var mode = args[1];
var delay = int.Parse(args[2]);
var count = int.Parse(args[3]);
var warmups = int.Parse(args[4]);
var fault = args.Length > 5 ? args[5] : null;
Directory.CreateDirectory(output);
TraceProbe.AwaitReturn = mode == "awaited";
TraceProbe.Enabled = fault != "trace-off";
var clock = Stopwatch.GetTimestamp();
double Now() => (Stopwatch.GetTimestamp() - clock) * 1000d / Stopwatch.Frequency;
var logger = new ResetLog(clock);
var result = new Result { Mode = mode, AddedWaitMilliseconds = delay, ExpectedCycles = count,
    Warmups = warmups, Prefix = "tt039r2_" + Guid.NewGuid().ToString("N") };
await File.WriteAllTextAsync(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { result.Prefix }));
var samples = new ConcurrentQueue<Sample>();
var states = new ConcurrentQueue<State>();
TtFactory<BenchmarkContext>? fixture = null;
Task? monitor = null;
using var stop = new CancellationTokenSource();
var known = new Dictionary<string, uint>();
try
{
    var start = Now();
    fixture = await Tt.CreateFactoryAsync(BenchmarkWorkload.Baseline, options =>
    {
        options.PoolSize = 8; options.StartupWarmDatabases = 8;
        options.EnableDynamicScaling = false; options.WarmFloor = 8;
        options.PreparationConcurrency = 1; options.ResetConcurrency = 4;
        options.MaxConnectionsPerDatabase = 4; options.MaxAdministratorConnections = 4;
        options.MemoryLimitMb = 1024; options.MaxContainers = 1;
        options.DockerHost = "unix:///var/run/docker.sock";
        options.DockerImage = "postgres:16-alpine"; options.ContainerPrefix = result.Prefix;
        options.Logger = logger; options.LogLevel = LogLevel.Debug;
        options.StartupTimeout = TimeSpan.FromMinutes(2); options.DrainTimeout = TimeSpan.FromMinutes(2);
    });
    result.StartupMilliseconds = Now() - start;
    result.Budget = fixture.ResourceBudget;
    var f = fixture;
    monitor = Task.Run(async () =>
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                states.Enqueue(new(Now(), f.PoolState));
                await Task.Delay(20, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    });
    start = Now();
    await AuditAsync(initial: true, mutate: true);
    await RunAsync(warmups, 0, measured: false);
    await SettleAsync(8 + warmups);
    result.PreflightMilliseconds = Now() - start;
    result.PreflightState = fixture.PoolState;
    var before = logger.Count;
    result.SuiteStartedMilliseconds = Now();
    await RunAsync(count, delay, measured: true);
    result.LastTestFinishedMilliseconds = Now();
    await SettleAsync(before + count);
    result.FinalResetsFinishedMilliseconds = Now();
    result.SuiteMilliseconds = result.FinalResetsFinishedMilliseconds - result.SuiteStartedMilliseconds;
    result.FinalResetWaitMilliseconds = result.FinalResetsFinishedMilliseconds - result.LastTestFinishedMilliseconds;
    result.FinalState = fixture.PoolState;
    start = Now();
    await AuditAsync(initial: false, mutate: false);
    await SettleAsync(before + count + 8);
    result.FinalAuditMilliseconds = Now() - start;
    result.Succeeded = true;
}
catch (Exception error)
{
    result.Failures.Add(error.GetType().Name + ": " + error.Message);
}
finally
{
    stop.Cancel();
    if (monitor is not null)
        try { await monitor; } catch (Exception error) { result.Failures.Add("Monitor: " + error.GetType().Name); }
    var start = Now();
    if (fixture is not null)
        try { await fixture.DisposeAsync(); result.CleanupCompleted = true; }
        catch (Exception error) { result.Failures.Add("Shutdown: " + error.GetType().Name + ": " + error.Message); }
    result.DisposalMilliseconds = Now() - start;
    result.TotalMilliseconds = Now();
    result.Succeeded &= result.Failures.Count == 0 && result.CleanupCompleted;
    result.OriginTick = clock;
    result.Frequency = Stopwatch.Frequency;
    result.Boundaries = TraceProbe.Events.ToArray();
    result.Samples = samples.OrderBy(s => s.Ordinal).ToArray();
    result.Resets = logger.Events.ToArray();
    result.States = states.ToArray();
    result.KnownDatabases = known;
    await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(result));
    Console.WriteLine(JsonSerializer.Serialize(new { result.Succeeded, result.SuiteMilliseconds,
        result.TotalMilliseconds, Cycles = result.Samples.Length, Resets = result.Resets.Length, result.Failures }));
}
return result.Succeeded ? 0 : 1;

async Task AuditAsync(bool initial, bool mutate)
{
    var leases = new List<TtLease<BenchmarkContext>>();
    try
    {
        for (var i = 0; i < 8; i++)
        {
            TraceProbe.Request.Value = $"audit-{(initial ? "pre" : "post")}/{i}";
            leases.Add(await fixture!.CreateLeaseAsync());
        }
        var seen = new HashSet<string>();
        foreach (var (lease, i) in leases.Select((lease, i) => (lease, i)))
        {
            var identity = await ReadIdentityAsync(lease.Context);
            if (!seen.Add(identity.Name)) throw new InvalidDataException("DatabaseAlias: simultaneous leases share a database");
            if (initial) known.Add(identity.Name, identity.Oid);
            else RequireIdentity(identity);
            await BenchmarkWorkload.VerifyBaselineAsync(lease.Context);
            foreach (var (table, value) in new[] { ("Parents", 100), ("Children", 200), ("Notes", 300) })
            {
                var sequenceSql = $"SELECT last_value::text || ':' || is_called::text AS \"Value\" FROM public.\"{table}_Id_seq\"";
                var sequence = await lease.Context.Database.SqlQueryRaw<string>(sequenceSql).SingleAsync();
                if (sequence != $"{value}:true") throw new InvalidDataException("SequenceAudit: baseline sequence differs");
            }
            if (initial && i == 0)
            {
                result.PostgreSqlVersion = await lease.Context.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").SingleAsync();
                var settings = await lease.Context.Database.SqlQueryRaw<string>("""
                    SELECT json_object_agg(name, setting)::text AS "Value" FROM pg_settings
                    WHERE name IN ('fsync','synchronous_commit','full_page_writes','wal_level','jit',
                        'autovacuum','shared_buffers','max_connections','track_io_timing','track_wal_io_timing')
                    """).SingleAsync();
                result.PostgreSqlSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(settings);
            }
            if (mutate) await BenchmarkWorkload.LoadAsync(lease.Context, WorkloadMix.ForTotal(130), "isolation-" + i);
        }
        if (mutate)
            foreach (var (lease, i) in leases.Select((lease, i) => (lease, i)))
                await BenchmarkWorkload.ValidateMutatedAsync(lease.Context, WorkloadMix.ForTotal(130), "isolation-" + i);
        result.IsolationChecked |= mutate;
    }
    finally
    {
        await Task.WhenAll(leases.Select(async (l, i) =>
        {
            TraceProbe.Request.Value = $"audit-{(initial ? "pre" : "post")}/{i}";
            await l.DisposeAsync();
        }));
    }
}

async Task RunAsync(int cycles, int wait, bool measured)
{
    await Task.WhenAll(Enumerable.Range(0, 4).Select(async worker =>
    {
        for (var ordinal = worker; ordinal < cycles; ordinal += 4)
        {
            TraceProbe.Request.Value = $"{(measured ? "measured" : "warmup")}/{ordinal}";
            var began = Now();
            await using var lease = await fixture!.CreateLeaseAsync();
            var acquired = Now();
            var identity = await ReadIdentityAsync(lease.Context);
            RequireIdentity(identity);
            await BenchmarkWorkload.VerifyBaselineAsync(lease.Context);
            var marker = $"{(measured ? "measured" : "warmup")}-{ordinal}";
            var mix = WorkloadMix.ForTotal(130);
            await BenchmarkWorkload.LoadAsync(lease.Context, mix, marker);
            if (measured && ordinal == 0 && fault == "distribution")
                await lease.Context.Notes.ExecuteDeleteAsync();
            await BenchmarkWorkload.ValidateMutatedAsync(lease.Context, mix, marker);
            if (wait > 0) await Task.Delay(wait);
            var returning = Now();
            await lease.DisposeAsync();
            var returned = Now();
            if (measured) samples.Enqueue(new(ordinal, worker, identity.Name, identity.Oid,
                began, acquired, returning, returned));
        }
    }));
}

async Task SettleAsync(int expected)
{
    var start = Now();
    while (Now() - start < 120_000)
    {
        var s = fixture!.PoolState;
        if (logger.Count == expected && s.Ready == 8 && s.Reserved == 8 && s.Leased == 0 &&
            s.WaitingResets == 0 && s.ExecutingResets == 0 && s.ExecutingCleanup == 0 &&
            s.Preparing == 0 && s.Quarantined == 0 && s.Retiring == 0 && s.Parked == 0 &&
            s.Parking == 0 && s.Rewarming == 0 && s.QueuedAcquisitions == 0 && TraceProbe.ActiveReturns == 0)
        {
            foreach (var reset in logger.Events)
                if (!reset.Succeeded || reset.Strategy != "Rows")
                    throw new InvalidDataException("ResetOutcome: expected successful row restoration");
            return;
        }
        if (logger.Count > expected) throw new InvalidDataException("ResetCount: more reset callbacks than returns");
        await Task.Delay(1);
    }
    throw new TimeoutException($"ResetSettlement: expected {expected}, observed {logger.Count}; {fixture!.PoolState}");
}

void RequireIdentity(Identity identity)
{
    if (!known.TryGetValue(identity.Name, out var oid) || oid != identity.Oid)
        throw new InvalidDataException("DatabaseIdentity: unexpected database or changed OID");
}

static async Task<Identity> ReadIdentityAsync(BenchmarkContext context) =>
    await context.Database.SqlQueryRaw<Identity>("SELECT current_database() AS \"Name\", oid::bigint AS \"Oid\" FROM pg_database WHERE datname=current_database()").SingleAsync();

sealed record Identity(string Name, uint Oid);
sealed record Sample(int Ordinal, int Worker, string Database, uint Oid, double Started,
    double Acquired, double Returning, double Returned);
sealed record State(double At, TtPoolSnapshot Snapshot);
sealed record Reset(double At, string Strategy, bool Succeeded, double PermitWaitMilliseconds,
    double ExecutionMilliseconds, double ValidationMilliseconds);
sealed class ResetLog(long origin) : ILogger
{
    public ConcurrentQueue<Reset> Events { get; } = new();
    public int Count => Events.Count;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= LogLevel.Debug;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (id.Id != 400) return;
        var fields = ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary(p => p.Key, p => p.Value);
        Events.Enqueue(new(Stopwatch.GetElapsedTime(origin).TotalMilliseconds,
            (string)fields["Strategy"]!, (bool)fields["Succeeded"]!,
            Convert.ToDouble(fields["ResetPermitWaitMilliseconds"]),
            Convert.ToDouble(fields["ResetExecutionMilliseconds"]),
            Convert.ToDouble(fields["ValidationMilliseconds"])));
    }
}
sealed class Result
{
    public required string Mode { get; init; }
    public required string Prefix { get; init; }
    public int AddedWaitMilliseconds { get; init; }
    public int ExpectedCycles { get; init; }
    public int Warmups { get; init; }
    public bool Succeeded { get; set; }
    public bool CleanupCompleted { get; set; }
    public bool IsolationChecked { get; set; }
    public string? PostgreSqlVersion { get; set; }
    public Dictionary<string, string>? PostgreSqlSettings { get; set; }
    public TtResourceBudget? Budget { get; set; }
    public TtPoolSnapshot? PreflightState { get; set; }
    public TtPoolSnapshot? FinalState { get; set; }
    public double StartupMilliseconds { get; set; }
    public double PreflightMilliseconds { get; set; }
    public double SuiteStartedMilliseconds { get; set; }
    public double LastTestFinishedMilliseconds { get; set; }
    public double FinalResetsFinishedMilliseconds { get; set; }
    public double SuiteMilliseconds { get; set; }
    public double FinalResetWaitMilliseconds { get; set; }
    public double FinalAuditMilliseconds { get; set; }
    public double DisposalMilliseconds { get; set; }
    public double TotalMilliseconds { get; set; }
    public List<string> Failures { get; } = [];
    public long OriginTick { get; set; }
    public long Frequency { get; set; }
    public TraceProbe.Boundary[] Boundaries { get; set; } = [];
    public Sample[] Samples { get; set; } = [];
    public Reset[] Resets { get; set; } = [];
    public State[] States { get; set; } = [];
    public Dictionary<string, uint> KnownDatabases { get; set; } = [];
}

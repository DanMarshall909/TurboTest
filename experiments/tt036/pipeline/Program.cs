using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using CompetitiveBaseline;
using Npgsql;
using Respawn;
using Respawn.Graph;

var mode = args[0];
var count = int.Parse(args[1]);
var delay = int.Parse(args[2]);
var output = args[3];
var fault = args.Length > 4 && args[4] == "fault";
Workload.Require(mode is "direct" or "awaited" or "background" && count > 0 && count % 4 == 0 && delay >= 0, "Arguments");
Workload.Require(!fault || mode == "background", "FaultMode");
var cs = Environment.GetEnvironmentVariable("TT036_CONNECTION") ?? throw new InvalidOperationException("ConnectionMissing");
var report = new Dictionary<string, object?> { ["Mode"] = mode, ["Count"] = count, ["DelayMs"] = delay,
    ["TestWorkers"] = 4, ["SlotCount"] = mode == "direct" ? 4 : 8, ["FaultInjection"] = fault, ["Succeeded"] = false };
var slots = new List<Slot>();
ReadyPool? pool = null;
var process = Stopwatch.StartNew();
try
{
    await using var root = new NpgsqlConnection(cs);
    await root.OpenAsync();
    await Workload.Execute(root, "CREATE ROLE tt036_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD 'tt036-disposable'");
    for (var i = 0; i < (int)report["SlotCount"]!; i++)
    {
        await Workload.Execute(root, $"CREATE DATABASE worker{i}");
        var slot = new Slot(i, new NpgsqlConnectionStringBuilder(cs) { Database = $"worker{i}" }.ConnectionString);
        slots.Add(slot); await slot.Prepare();
    }
    report["PreparationMs"] = process.Elapsed.TotalMilliseconds;
    if (mode != "direct") pool = new ReadyPool(slots);
    var warm = Stopwatch.GetTimestamp();
    if (pool is null) await Batches.Direct(slots, 20, delay);
    else await pool.Run(20, delay, mode == "awaited", false);
    report["WarmupMs"] = Stopwatch.GetElapsedTime(warm).TotalMilliseconds;
    foreach (var slot in slots) await slot.CheckBaseline();
    report["Settings"] = JsonDocument.Parse(await Workload.Scalar<string>(root, """
        SELECT json_object_agg(name,setting)::text FROM pg_settings WHERE name=ANY(ARRAY[
        'server_version','fsync','synchronous_commit','full_page_writes','wal_level','max_wal_senders','archive_mode',
        'jit','autovacuum','shared_buffers','max_connections','session_replication_role','default_transaction_isolation',
        'shared_preload_libraries','track_io_timing','track_wal_io_timing'])
        """)).RootElement.Clone();
    var batch = pool is null ? await Batches.Direct(slots, count, delay) : await pool.Run(count, delay, mode == "awaited", fault);
    report["Batch"] = batch;
    var audit = Stopwatch.GetTimestamp();
    foreach (var slot in slots) await slot.CheckBaseline();
    report["FinalAuditMs"] = Stopwatch.GetElapsedTime(audit).TotalMilliseconds;
    report["Succeeded"] = true;
}
catch (Exception e)
{
    report["Failure"] = e.ToString();
    if (pool?.FaultedSlot is { } failed)
    {
        try { report["FaultedSlotParentRows"] = await Workload.Scalar<long>(failed.Admin, "SELECT count(*) FROM \"Parents\""); }
        catch (Exception auditError) { report["FaultAuditFailure"] = auditError.ToString(); }
    }
}
finally
{
    var errors = new List<string>();
    if (pool is not null)
    {
        try { await pool.DisposeAsync(); } catch (Exception e) { errors.Add(e.ToString()); }
        report["PoolSettlement"] = pool.Snapshot();
    }
    foreach (var slot in slots)
    {
        try { await slot.DisposeAsync(); } catch (Exception e) { errors.Add($"slot{slot.Id}: {e}"); }
    }
    if (errors.Count > 0) { report["Succeeded"] = false; report["DisposalFailures"] = errors; }
}
report["Slots"] = slots.Select(s => new { s.Id, s.DeleteSql, s.Qualification, s.State }).ToArray();
report["ProcessMs"] = process.Elapsed.TotalMilliseconds;
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { mode, count, delay, succeeded = report["Succeeded"], failure = report.GetValueOrDefault("Failure") }));
return (bool)report["Succeeded"]! ? 0 : 1;

sealed class Sample
{
    public int Ordinal { get; init; }
    public int SlotId { get; set; }
    public double RequestedMs { get; init; }
    public double AcquiredMs { get; set; }
    public double ApplicationEndMs { get; set; }
    public double ReturnedMs { get; set; }
    public double ResetStartMs { get; set; }
    public double ResetEndMs { get; set; }
    public double TestFinishedMs { get; set; }
    public double LeaseWaitMs => AcquiredMs - RequestedMs;
    public double ApplicationMs => ApplicationEndMs - AcquiredMs;
    public double ResetQueueMs => ResetStartMs - ReturnedMs;
    public double ResetMs => ResetEndMs - ResetStartMs;
}
sealed record Batch(double SuiteMs, double TestsFinishedMs, double FinalDrainMs, int ReadySlots,
    int? ResetQueueHighWater, int? ActiveResetHighWater, Sample[] Samples);

static class Batches
{
    public static double Now(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    public static async Task<Batch> Direct(List<Slot> slots, int count, int delay)
    {
        var start = Stopwatch.GetTimestamp();
        var result = await Task.WhenAll(Enumerable.Range(0, 4).Select(async worker =>
        {
            var samples = new List<Sample>(); var slot = slots[worker];
            for (var i = 0; i < count / 4; i++)
            {
                var sample = new Sample { Ordinal = worker * (count / 4) + i, SlotId = slot.Id, RequestedMs = Now(start) };
                sample.AcquiredMs = Now(start);
                await slot.Application("test" + sample.Ordinal, delay);
                sample.ApplicationEndMs = Now(start); sample.ReturnedMs = Now(start);
                sample.ResetStartMs = Now(start); await slot.Reset(false); sample.ResetEndMs = Now(start);
                sample.TestFinishedMs = Now(start); samples.Add(sample);
            }
            return samples;
        }));
        var done = Now(start);
        return new Batch(done, done, 0, slots.Count, null, null, result.SelectMany(s => s).OrderBy(s => s.Ordinal).ToArray());
    }
}

sealed class ReadyPool : IAsyncDisposable
{
    sealed record Job(Slot Slot, Sample Sample, long Started, TaskCompletionSource Completion, bool Inject);
    readonly List<Slot> slots;
    readonly Channel<Slot> ready;
    readonly Channel<Job> dirty;
    readonly Task[] resetters;
    Exception? failure;
    int queued, active, queuePeak, activePeak, failedReadyPublications;
    bool stopped;
    public Slot? FaultedSlot { get; private set; }
    public ReadyPool(List<Slot> owned)
    {
        slots = owned;
        ready = Channel.CreateBounded<Slot>(new BoundedChannelOptions(owned.Count) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        dirty = Channel.CreateBounded<Job>(new BoundedChannelOptions(owned.Count) { FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        foreach (var slot in owned) Workload.Require(ready.Writer.TryWrite(slot), "InitialReadyCapacity");
        resetters = Enumerable.Range(0, 4).Select(_ => ResetLoop()).ToArray();
    }
    static void Peak(ref int peak, int value)
    {
        int observed;
        do { observed = Volatile.Read(ref peak); if (value <= observed) return; }
        while (Interlocked.CompareExchange(ref peak, value, observed) != observed);
    }
    static void Transition(Slot slot, int expected, int next) => Workload.Require(Interlocked.CompareExchange(ref slot.State, next, expected) == expected, "OwnedSlotTransition");
    void Fail(Exception e)
    {
        Interlocked.CompareExchange(ref failure, e, null);
        ready.Writer.TryComplete(e); // Wake blocked acquisitions. Existing in-flight work is still joined.
    }
    async Task ResetLoop()
    {
        await foreach (var job in dirty.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref queued);
            Exception? error = null;
            job.Sample.ResetStartMs = Batches.Now(job.Started);
            Peak(ref activePeak, Interlocked.Increment(ref active));
            try
            {
                if (Volatile.Read(ref failure) is { } prior) throw new InvalidOperationException("BatchAlreadyFailed", prior);
                if (job.Inject) FaultedSlot = job.Slot;
                await job.Slot.Reset(job.Inject);
                job.Sample.ResetEndMs = Batches.Now(job.Started);
                if (Volatile.Read(ref failure) is { } late) throw new InvalidOperationException("BatchAlreadyFailed", late);
                Transition(job.Slot, 2, 0);
                if (ReferenceEquals(FaultedSlot, job.Slot)) Interlocked.Increment(ref failedReadyPublications);
                Workload.Require(ready.Writer.TryWrite(job.Slot), "ReadyPublication");
            }
            catch (Exception e)
            {
                error = e; Interlocked.Exchange(ref job.Slot.State, 3);
                Fail(e);
            }
            finally { Interlocked.Decrement(ref active); }
            if (error is null) job.Completion.TrySetResult();
            else job.Completion.TrySetException(error);
        }
    }
    public async Task<Batch> Run(int count, int delay, bool awaitReturn, bool inject)
    {
        Workload.Require(queued == 0 && active == 0 && slots.All(s => s.State == 0) && ready.Reader.Count == slots.Count, "RestoredStartCapacity");
        queuePeak = activePeak = 0;
        var start = Stopwatch.GetTimestamp();
        var tickets = new ConcurrentBag<Task>();
        var samples = new ConcurrentBag<Sample>();
        var injection = inject ? 1 : 0;
        Exception? batchFailure = null;
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 4).Select(async worker =>
            {
                for (var i = 0; i < count / 4; i++)
                {
                    if (Volatile.Read(ref failure) is { } prior) throw new InvalidOperationException("BatchAlreadyFailed", prior);
                    var sample = new Sample { Ordinal = worker * (count / 4) + i, RequestedMs = Batches.Now(start) };
                    var slot = await ready.Reader.ReadAsync();
                    Transition(slot, 0, 1); sample.SlotId = slot.Id; sample.AcquiredMs = Batches.Now(start);
                    Task returned;
                    try { await slot.Application("test" + sample.Ordinal, delay); }
                    catch (Exception e) { Fail(e); throw; }
                    finally
                    {
                        sample.ApplicationEndMs = Batches.Now(start);
                        Transition(slot, 1, 2); sample.ReturnedMs = Batches.Now(start);
                        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        returned = completion.Task; tickets.Add(returned); samples.Add(sample);
                        Peak(ref queuePeak, Interlocked.Increment(ref queued));
                        var job = new Job(slot, sample, start, completion, Interlocked.Exchange(ref injection, 0) == 1);
                        if (!dirty.Writer.TryWrite(job))
                        {
                            Interlocked.Decrement(ref queued); Interlocked.Exchange(ref slot.State, 3);
                            var e = new InvalidOperationException("BoundedResetQueueOverflow"); completion.TrySetException(e); Fail(e);
                        }
                    }
                    if (awaitReturn) await returned;
                    sample.TestFinishedMs = Batches.Now(start);
                }
            }));
        }
        catch (Exception e) { batchFailure = e; Fail(e); }
        var testsFinished = Batches.Now(start);
        try { await Task.WhenAll(tickets); } catch (Exception e) { batchFailure ??= e; }
        if (batchFailure is not null) throw new InvalidOperationException("BatchResetOrApplicationFailure", batchFailure);
        Workload.Require(queued == 0 && active == 0 && slots.All(s => s.State == 0) && ready.Reader.Count == slots.Count, "RestoredEndCapacity");
        var done = Batches.Now(start);
        return new Batch(done, testsFinished, done - testsFinished, ready.Reader.Count, queuePeak, activePeak, samples.OrderBy(s => s.Ordinal).ToArray());
    }
    public object Snapshot() => new { Queued = queued, ActiveResets = active, Stopped = stopped,
        ReadySlots = slots.Count(s => s.State == 0), FailedSlots = slots.Count(s => s.State == 3),
        FaultedSlotId = FaultedSlot?.Id, FailedSlotReadyPublications = failedReadyPublications };
    public async ValueTask DisposeAsync() { dirty.Writer.TryComplete(); await Task.WhenAll(resetters); stopped = true; }
}

sealed class Slot(int id, string cs) : IAsyncDisposable
{
    public int Id => id;
    public int State; // 0 Ready, 1 Leased, 2 Waiting/executing reset, 3 Failed (never reused).
    public NpgsqlConnection Admin { get; } = new(cs);
    public string? DeleteSql { get; private set; }
    public string? Qualification { get; private set; }
    readonly NpgsqlDataSource app = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { Username = "tt036_app", MaxPoolSize = 3, ApplicationName = "tt036_app" }.ConnectionString);
    Respawner respawner = null!;
    public async Task Prepare()
    {
        await Admin.OpenAsync(); await Workload.Execute(Admin, "GRANT CREATE ON SCHEMA public TO tt036_app");
        await using (var c = await app.OpenConnectionAsync()) await Workload.Execute(c, Workload.Schema + Workload.SeedSql);
        respawner = await Respawner.CreateAsync(Admin, new() { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["public"], TablesToIgnore = [new Table("SchemaVersion")], CommandTimeout = 15 });
        DeleteSql = respawner.DeleteSql;
        for (var i = 0; i < 3; i++) { await Application("qualification" + i, 0, true); await Reset(false); await CheckBaseline(); }
        await using (var c = await app.OpenConnectionAsync())
        {
            foreach (var (sql, state) in new[] {
                ("INSERT INTO \"Children\" VALUES(99999,99999,'bad-fk',false)", "23503"),
                ("INSERT INTO \"Parents\" VALUES(1,'duplicate',0)", "23505"),
                ("INSERT INTO \"Parents\" VALUES(99999,NULL,0)", "23502") })
            {
                var rejected = false;
                try { await Workload.Execute(c, sql); } catch (PostgresException e) when (e.SqlState == state) { rejected = true; }
                Workload.Require(rejected, "ConstraintEnforced:" + state);
            }
        }
        await Reset(false); await CheckBaseline();
        Qualification = "Three committed multi-connection cycles, exact baseline/sequence restoration, distinct sessions and FK/PK/NOT NULL rejection passed";
    }
    public async Task Application(string marker, int delay, bool qualify = false)
    {
        await using var a = await app.OpenConnectionAsync();
        await using var b = await app.OpenConnectionAsync();
        await using var c = await app.OpenConnectionAsync();
        if (qualify) Workload.Require(a.ProcessID != b.ProcessID && a.ProcessID != c.ProcessID && b.ProcessID != c.ProcessID, "DistinctConnections");
        await Workload.Mutate(a, b, marker); await Workload.CheckMutated(c, marker);
        if (delay > 0) await Task.Delay(delay); // Explicit synthetic application/external time, not database CPU work.
    }
    public async Task Reset(bool inject)
    {
        await respawner.ResetAsync(Admin);
        if (inject) throw new InvalidOperationException("TT036InjectedFailureAfterCommittedTruncate");
        await Workload.Execute(Admin, Workload.SeedSql);
    }
    public Task CheckBaseline() => Workload.CheckBaseline(Admin);
    public async ValueTask DisposeAsync() { try { await app.DisposeAsync(); } finally { await Admin.DisposeAsync(); } }
}

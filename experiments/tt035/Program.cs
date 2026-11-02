using System.Diagnostics;
using System.Text.Json;
using CompetitiveBaseline;
using Npgsql;
using Respawn;
using Respawn.Graph;

var contender = args[0];
var count = int.Parse(args[1]);
var output = args[2];
const int parallelism = 4;
Workload.Require(contender is "cached" or "respawn" && count > 0 && count % parallelism == 0, "Arguments");
var report = new Dictionary<string, object?> { ["Contender"] = contender, ["Count"] = count, ["Parallelism"] = parallelism, ["Succeeded"] = false };
var workers = new List<Worker>();
var timer = Stopwatch.StartNew();
var cs = Environment.GetEnvironmentVariable("TT035_CONNECTION") ?? throw new InvalidOperationException("ConnectionMissing");
try
{
    await using var root = new NpgsqlConnection(cs);
    await root.OpenAsync();
    await Workload.Execute(root, "CREATE ROLE tt035_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD 'tt035-disposable'");
    for (var i = 0; i < parallelism; i++)
    {
        await Workload.Execute(root, $"CREATE DATABASE worker{i}");
        var worker = new Worker(i, contender, new NpgsqlConnectionStringBuilder(cs) { Database = $"worker{i}" }.ConnectionString);
        workers.Add(worker);
        await worker.Prepare();
    }
    report["PreparationMs"] = timer.Elapsed.TotalMilliseconds;
    var warm = Stopwatch.GetTimestamp();
    await Task.WhenAll(workers.Select(w => w.Run(5, false)));
    report["WarmupMs"] = Stopwatch.GetElapsedTime(warm).TotalMilliseconds;
    report["Settings"] = JsonDocument.Parse(await Workload.Scalar<string>(root, """
        SELECT json_object_agg(name,setting)::text FROM pg_settings WHERE name = ANY(ARRAY[
        'server_version','fsync','synchronous_commit','full_page_writes','wal_level','max_wal_senders','archive_mode',
        'jit','autovacuum','checkpoint_timeout','max_wal_size','shared_buffers','max_connections','data_checksums',
        'session_replication_role','default_transaction_isolation','log_statement','log_min_duration_statement'])
        """)).RootElement.Clone();
    var suite = Stopwatch.GetTimestamp();
    await Task.WhenAll(workers.Select(w => w.Run(count / parallelism, true)));
    report["SuiteMs"] = Stopwatch.GetElapsedTime(suite).TotalMilliseconds;
    var audit = Stopwatch.GetTimestamp();
    foreach (var w in workers) await w.CheckBaseline();
    report["FinalAuditMs"] = Stopwatch.GetElapsedTime(audit).TotalMilliseconds;
    report["Succeeded"] = true;
}
catch (Exception e) { report["Failure"] = e.ToString(); }
finally
{
    var failures = new List<string>();
    foreach (var w in workers)
    {
        try { await w.DisposeAsync(); }
        catch (Exception e) { failures.Add($"worker{w.Index}: {e}"); }
    }
    if (failures.Count > 0) { report["Succeeded"] = false; report["DisposalFailures"] = failures; }
}
report["Workers"] = workers.Select(w => new { w.Index, w.DeleteSql, w.CachedSql, w.Qualification, w.Samples }).ToArray();
report["ProcessMs"] = timer.Elapsed.TotalMilliseconds;
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { contender, count, succeeded = report["Succeeded"], suiteMs = report.GetValueOrDefault("SuiteMs"), failure = report.GetValueOrDefault("Failure") }));
return (bool)report["Succeeded"]! ? 0 : 1;

sealed class Worker(int index, string contender, string cs) : IAsyncDisposable
{
    public int Index => index;
    public string? DeleteSql { get; private set; }
    public string? CachedSql { get; private set; }
    public string? Qualification { get; private set; }
    public List<object> Samples { get; } = [];
    readonly NpgsqlConnection admin = new(cs);
    readonly NpgsqlDataSource app = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { Username = "tt035_app", MaxPoolSize = 3, ApplicationName = "tt035_app" }.ConnectionString);
    Respawner respawner = null!;

    public async Task Prepare()
    {
        await admin.OpenAsync();
        await Workload.Execute(admin, "GRANT CREATE ON SCHEMA public TO tt035_app");
        await using (var c = await app.OpenConnectionAsync())
        {
            await Workload.Execute(c, Workload.Schema + Workload.SeedSql);
            await Workload.CheckBaseline(c);
        }
        respawner = await Respawner.CreateAsync(admin, new() { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["public"], TablesToIgnore = [new Table("SchemaVersion")], CommandTimeout = 15 });
        DeleteSql = respawner.DeleteSql ?? throw new InvalidOperationException("MissingResetPlan");
        CachedSql = "BEGIN;\n" + DeleteSql + "\n" + Workload.SeedSql + "\nCOMMIT;";
        for (var i = 0; i < 3; i++) { await Cycle(-i - 1, true); await CheckBaseline(); }
        await using (var c = await app.OpenConnectionAsync())
        {
            var rejected = false;
            try { await Workload.Execute(c, "INSERT INTO \"Children\"(\"Id\",\"ParentId\",\"Name\",\"Enabled\") VALUES (99999,99999,'bad-fk',false)"); }
            catch (PostgresException e) when (e.SqlState == "23503") { rejected = true; }
            Workload.Require(rejected, "ForeignKeyStillEnforced");
        }
        await Reset();
        await CheckBaseline();
        Qualification = "Three committed multi-connection cycles, exact baseline rows/sequences, distinct physical connections and FK rejection passed";
    }
    public Task CheckBaseline() => Workload.CheckBaseline(admin);
    async Task Reset()
    {
        if (contender == "cached") await Workload.Execute(admin, CachedSql!);
        else { await respawner.ResetAsync(admin); await Workload.Execute(admin, Workload.SeedSql); }
    }
    async Task<object> Cycle(int ordinal, bool qualify = false)
    {
        var cycle = Stopwatch.GetTimestamp();
        double open, application;
        await using (var a = await app.OpenConnectionAsync())
        await using (var b = await app.OpenConnectionAsync())
        await using (var c = await app.OpenConnectionAsync())
        {
            open = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds;
            if (qualify) Workload.Require(a.ProcessID != b.ProcessID && b.ProcessID != c.ProcessID && a.ProcessID != c.ProcessID, "DistinctConnections");
            var work = Stopwatch.GetTimestamp();
            var marker = $"worker{index}-case{ordinal}";
            await Workload.Mutate(a, b, marker);
            await Workload.CheckMutated(c, marker);
            application = Stopwatch.GetElapsedTime(work).TotalMilliseconds;
        }
        var reset = Stopwatch.GetTimestamp();
        await Reset();
        return new { Ordinal = ordinal, OpenMs = open, ApplicationMs = application, ResetMs = Stopwatch.GetElapsedTime(reset).TotalMilliseconds, CycleMs = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds };
    }
    public async Task Run(int count, bool record)
    {
        for (var i = 0; i < count; i++)
        {
            var sample = await Cycle(i);
            if (record) Samples.Add(sample);
        }
    }
    public async ValueTask DisposeAsync()
    {
        try { await app.DisposeAsync(); }
        finally { await admin.DisposeAsync(); }
    }
}

using System.Diagnostics;
using System.Text.Json;
using CompetitiveBaseline;
using Npgsql;
using Respawn;
using Respawn.Graph;

var contender = args[0];
var count = int.Parse(args[1]);
var output = args[2];
var unlogged = args[3] == "unlogged";
var diagnostic = args[4] == "profile";
var parallelism = diagnostic ? 1 : 4;
Workload.Require(contender is "cached" or "respawn" && count > 0 && count % parallelism == 0, "Arguments");
var report = new Dictionary<string, object?> { ["Contender"] = contender, ["Count"] = count, ["Parallelism"] = parallelism, ["Unlogged"] = unlogged, ["Diagnostic"] = diagnostic, ["Succeeded"] = false };
var workers = new List<Worker>();
var timer = Stopwatch.StartNew();
var cs = Environment.GetEnvironmentVariable("TT036_CONNECTION") ?? throw new InvalidOperationException("ConnectionMissing");
try
{
    await using var root = new NpgsqlConnection(cs);
    await root.OpenAsync();
    if (diagnostic)
    {
        await Workload.Execute(root, "CREATE EXTENSION pg_stat_statements");
        var version = await Workload.Scalar<string>(root, "SELECT extversion FROM pg_extension WHERE extname='pg_stat_statements'");
        Workload.Require(version == "1.10", "ProfileExtensionVersion");
        report["ProfileExtensionVersion"] = version;
    }
    await Workload.Execute(root, "CREATE ROLE tt036_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD 'tt036-disposable'");
    for (var i = 0; i < parallelism; i++)
    {
        await Workload.Execute(root, $"CREATE DATABASE worker{i}");
        var worker = new Worker(i, contender, new NpgsqlConnectionStringBuilder(cs) { Database = $"worker{i}" }.ConnectionString, unlogged, diagnostic);
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
        'session_replication_role','default_transaction_isolation','log_statement','log_min_duration_statement',
        'shared_preload_libraries','track_io_timing','track_wal_io_timing','compute_query_id','pg_stat_statements.track',
        'pg_stat_statements.track_planning','pg_stat_statements.track_utility'])
        """)).RootElement.Clone();
    if (diagnostic)
    {
        foreach (var w in workers) await w.FlushStats();
        await Workload.Execute(root, "SELECT pg_stat_statements_reset(); SELECT pg_stat_force_next_flush()");
        await Workload.Execute(root, "SELECT pg_stat_clear_snapshot()");
        report["ProfileBefore"] = await Profile.Stats(root);
    }
    var suite = Stopwatch.GetTimestamp();
    await Task.WhenAll(workers.Select(w => w.Run(count / parallelism, true)));
    report["SuiteMs"] = Stopwatch.GetElapsedTime(suite).TotalMilliseconds;
    if (diagnostic)
    {
        foreach (var w in workers) await w.FlushStats();
        await Workload.Execute(root, "SELECT pg_stat_force_next_flush()");
        await Workload.Execute(root, "SELECT pg_stat_clear_snapshot()");
        report["ProfileAfter"] = await Profile.Stats(root);
        report["Statements"] = JsonDocument.Parse(await Workload.Scalar<string>(root, """
            SELECT coalesce(json_agg(s),'[]'::json)::text FROM (
              SELECT dbid,userid,toplevel,queryid,query,calls,total_exec_time,min_exec_time,max_exec_time,rows,
                shared_blks_hit,shared_blks_read,shared_blks_dirtied,shared_blks_written,
                temp_blks_read,temp_blks_written,blk_read_time,blk_write_time,wal_records,wal_fpi,wal_bytes
              FROM pg_stat_statements WHERE dbid IN (SELECT oid FROM pg_database WHERE datname LIKE 'worker%')
              ORDER BY total_exec_time DESC) s
            """)).RootElement.Clone();
    }
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
report["Workers"] = workers.Select(w => new { w.Index, w.DeleteSql, w.CachedSql, w.Qualification, w.Persistence, w.Samples }).ToArray();
report["ProcessMs"] = timer.Elapsed.TotalMilliseconds;
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { contender, count, succeeded = report["Succeeded"], suiteMs = report.GetValueOrDefault("SuiteMs"), failure = report.GetValueOrDefault("Failure") }));
return (bool)report["Succeeded"]! ? 0 : 1;

sealed class Worker(int index, string contender, string cs, bool unlogged, bool diagnostic) : IAsyncDisposable
{
    public int Index => index;
    public string? DeleteSql { get; private set; }
    public string? CachedSql { get; private set; }
    public string? Qualification { get; private set; }
    public JsonElement Persistence { get; private set; }
    public List<object> Samples { get; } = [];
    readonly NpgsqlConnection admin = new(cs);
    readonly NpgsqlDataSource app = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(cs) { Username = "tt036_app", MaxPoolSize = 3, ApplicationName = "tt036_app" }.ConnectionString);
    Respawner respawner = null!;

    public async Task Prepare()
    {
        await admin.OpenAsync();
        await Workload.Execute(admin, "GRANT CREATE ON SCHEMA public TO tt036_app");
        await using (var c = await app.OpenConnectionAsync())
        {
            var schema = unlogged ? Workload.Schema.Replace("CREATE TABLE", "CREATE UNLOGGED TABLE", StringComparison.Ordinal) : Workload.Schema;
            await Workload.Execute(c, schema + Workload.SeedSql);
            await Workload.CheckBaseline(c);
        }
        Persistence = JsonDocument.Parse(await Workload.Scalar<string>(admin, """
            SELECT json_agg(t)::text FROM (SELECT relname,relkind,relpersistence FROM pg_class
              WHERE relnamespace='public'::regnamespace AND relkind IN ('r','i','S') ORDER BY relname) t
            """)).RootElement.Clone();
        Workload.Require(Persistence.GetArrayLength() == 11 && Persistence.EnumerateArray().All(r => r.GetProperty("relpersistence").GetString() == (unlogged ? "u" : "p")), "TableIndexSequencePersistence");
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
            foreach (var (sql, state) in new[] {
                ("INSERT INTO \"Parents\" VALUES(1,'duplicate',0)", "23505"),
                ("INSERT INTO \"Parents\" VALUES(99999,NULL,0)", "23502") })
            {
                rejected = false;
                try { await Workload.Execute(c, sql); }
                catch (PostgresException e) when (e.SqlState == state) { rejected = true; }
                Workload.Require(rejected, "OrdinaryConstraintsStillEnforced:" + state);
            }
        }
        await Reset();
        await CheckBaseline();
        Qualification = "Three committed multi-connection cycles, exact baseline rows/sequences, distinct physical connections, FK/PK/NOT NULL rejection and table/index/sequence persistence passed";
    }
    public Task CheckBaseline() => Workload.CheckBaseline(admin);
    public async Task FlushStats()
    {
        await using var a = await app.OpenConnectionAsync();
        await using var b = await app.OpenConnectionAsync();
        await using var c = await app.OpenConnectionAsync();
        foreach (var connection in new[] { a, b, c, admin })
            await Workload.Execute(connection, "SELECT pg_stat_force_next_flush()");
    }
    async Task Reset()
    {
        if (contender == "cached") await Workload.Execute(admin, CachedSql!);
        else { await respawner.ResetAsync(admin); await Workload.Execute(admin, Workload.SeedSql); }
    }
    async Task<object> Cycle(int ordinal, bool qualify = false, bool profile = false)
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
        double? truncateTransaction = null, seed = null;
        if (profile && contender == "respawn")
        {
            await respawner.ResetAsync(admin);
            truncateTransaction = Stopwatch.GetElapsedTime(reset).TotalMilliseconds;
            var started = Stopwatch.GetTimestamp();
            await Workload.Execute(admin, Workload.SeedSql);
            seed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        else await Reset();
        return new { Ordinal = ordinal, OpenMs = open, ApplicationMs = application, ResetMs = Stopwatch.GetElapsedTime(reset).TotalMilliseconds, CycleMs = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds, TruncateTransactionMs = truncateTransaction, SeedMs = seed };
    }
    public async Task Run(int count, bool record)
    {
        for (var i = 0; i < count; i++)
        {
            var sample = await Cycle(i, profile: diagnostic && record);
            if (record) Samples.Add(sample);
        }
    }
    public async ValueTask DisposeAsync()
    {
        try { await app.DisposeAsync(); }
        finally { await admin.DisposeAsync(); }
    }
}

static class Profile
{
    public static async Task<JsonElement> Stats(NpgsqlConnection c) => JsonDocument.Parse(await Workload.Scalar<string>(c, """
        SELECT json_build_object('statements_info',(SELECT row_to_json(s) FROM pg_stat_statements_info s),
          'wal',(SELECT row_to_json(w) FROM pg_stat_wal w),
          'bgwriter',(SELECT row_to_json(b) FROM pg_stat_bgwriter b),
          'io',(SELECT json_agg(i) FROM pg_stat_io i),
          'databases',(SELECT json_agg(d) FROM (SELECT datname,xact_commit,xact_rollback,blks_read,blks_hit,
            tup_inserted,tup_updated,tup_deleted,blk_read_time,blk_write_time FROM pg_stat_database WHERE datname LIKE 'worker%') d))::text
        """)).RootElement.Clone();
}

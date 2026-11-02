using System.Diagnostics;
using System.Text.Json;
using CompetitiveBaseline;
using Npgsql;
using Respawn;
using Respawn.Graph;

var contender = args[0];
var count = int.Parse(args[1]);
var output = args[2];
Workload.Require(contender is "emaj" or "respawn" && count > 0, "Arguments");
var report = new Dictionary<string, object?> { ["Contender"] = contender, ["Count"] = count, ["Succeeded"] = false };
var samples = new List<object>();
var timer = Stopwatch.StartNew();
var cs = Environment.GetEnvironmentVariable("TT034_CONNECTION") ?? throw new InvalidOperationException("ConnectionMissing");
try
{
    await using var adminSource = NpgsqlDataSource.Create(cs);
    await using var admin = await adminSource.OpenConnectionAsync();
    await Workload.Execute(admin, "CREATE ROLE tt034_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD 'tt034-disposable'; GRANT CREATE ON SCHEMA public TO tt034_app");
    var appCs = new NpgsqlConnectionStringBuilder(cs) { Username = "tt034_app", MaxPoolSize = 3, ApplicationName = "tt034_app" }.ConnectionString;
    await using var appSource = NpgsqlDataSource.Create(appCs);
    await using (var c = await appSource.OpenConnectionAsync())
    {
        await Workload.Execute(c, Workload.Schema + Workload.SeedSql);
        await Workload.CheckBaseline(c);
    }
    Respawner? respawner = null;
    if (contender == "emaj")
    {
        await Workload.Execute(admin, """
            CREATE EXTENSION emaj CASCADE;
            SELECT emaj.emaj_create_group('workload');
            SELECT emaj.emaj_assign_tables('public',ARRAY['Parents','Children','Notes'],'workload');
            SELECT emaj.emaj_assign_sequences('public',ARRAY['Parents_Id_seq','Children_Id_seq','Notes_Id_seq'],'workload');
            SELECT emaj.emaj_start_group('workload','baseline');
            """);
        Workload.Require(await Workload.Scalar<string>(admin, "SELECT extversion FROM pg_extension WHERE extname='emaj'") == "5.0.1", "EmajVersion");
    }
    else respawner = await Respawner.CreateAsync(admin, new() { DbAdapter = DbAdapter.Postgres, SchemasToInclude = ["public"], TablesToIgnore = [new Table("SchemaVersion")], CommandTimeout = 15 });

    async Task Reset()
    {
        if (respawner is not null) { await respawner.ResetAsync(admin); await Workload.Execute(admin, Workload.SeedSql); }
        else await Workload.Execute(admin, "SELECT * FROM emaj.emaj_rollback_group('workload','baseline')");
    }
    async Task CheckRestored()
    {
        // E-Maj uses RESTART WITH next_value: (1001,false) and (1000,true)
        // have the same next generated ID, though their raw catalog state differs.
        var state = await Workload.Scalar<string>(admin, """
            SELECT json_build_object('parents',(SELECT json_build_object('last_value',last_value,'is_called',is_called) FROM "Parents_Id_seq"),
              'children',(SELECT json_build_object('last_value',last_value,'is_called',is_called) FROM "Children_Id_seq"),
              'notes',(SELECT json_build_object('last_value',last_value,'is_called',is_called) FROM "Notes_Id_seq"))::text
            """);
        report["RestoredSequenceRepresentation"] = JsonDocument.Parse(state).RootElement.Clone();
        Workload.Require(await Workload.Scalar<bool>(admin, """
            SELECT
              (SELECT count(*)=32 AND bool_and("Id" BETWEEN 1 AND 32 AND "Name"='seed-parent-'||"Id" AND "Priority"="Id"*3) FROM "Parents") AND
              (SELECT count(*)=64 AND bool_and("Id" BETWEEN 1 AND 64 AND "ParentId"=1+("Id"-1)%32 AND "Name"='seed-child-'||"Id" AND "Enabled"=("Id"%2=0)) FROM "Children") AND
              (SELECT count(*)=128 AND bool_and("Id" BETWEEN 1 AND 128 AND "ChildId"=1+("Id"-1)%64 AND "Body"='seed-note-'||"Id" AND "Revision"="Id"*7) FROM "Notes") AND
              (SELECT CASE WHEN is_called THEN last_value+1 ELSE last_value END = 1001 FROM "Parents_Id_seq") AND
              (SELECT CASE WHEN is_called THEN last_value+1 ELSE last_value END = 2001 FROM "Children_Id_seq") AND
              (SELECT CASE WHEN is_called THEN last_value+1 ELSE last_value END = 3001 FROM "Notes_Id_seq") AND
              (SELECT count(*)=1 AND bool_and("Id"=1) FROM "SchemaVersion")
            """), "RestoredRowsAndNextSequenceValues");
    }
    async Task<object> Cycle(int i, bool checkConnections = false)
    {
        var cycle = Stopwatch.GetTimestamp();
        double open, work;
        await using (var a = await appSource.OpenConnectionAsync())
        await using (var b = await appSource.OpenConnectionAsync())
        await using (var c = await appSource.OpenConnectionAsync())
        {
            open = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds;
            if (checkConnections) Workload.Require(a.ProcessID != b.ProcessID && b.ProcessID != c.ProcessID && a.ProcessID != c.ProcessID, "DistinctConnections");
            var start = Stopwatch.GetTimestamp();
            await Workload.Mutate(a, b, "case" + i);
            await Workload.CheckMutated(c, "case" + i);
            work = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        var reset = Stopwatch.GetTimestamp();
        await Reset();
        return new { Ordinal = i, OpenMs = open, ApplicationMs = work, ResetMs = Stopwatch.GetElapsedTime(reset).TotalMilliseconds, CycleMs = Stopwatch.GetElapsedTime(cycle).TotalMilliseconds };
    }

    // Qualification is outside timing; baseline scans are never repeated in measured cycles.
    for (var i = 0; i < 3; i++)
    {
        await Cycle(-i - 1, checkConnections: true);
        await CheckRestored();
    }
    await using (var c = await appSource.OpenConnectionAsync())
    {
        var rejected = false;
        try { await Workload.Execute(c, "INSERT INTO \"Children\"(\"Id\",\"ParentId\",\"Name\",\"Enabled\") VALUES (99999,99999,'bad-fk',false)"); }
        catch (PostgresException e) when (e.SqlState == "23503") { rejected = true; }
        Workload.Require(rejected, "ForeignKeyStillEnforced");
    }
    await Reset();
    await CheckRestored();
    report["Qualification"] = "Three repeated committed multi-connection cycles, exact baseline rows/next sequence values and FK rejection passed";
    report["PreparationMs"] = timer.Elapsed.TotalMilliseconds;
    var warm = Stopwatch.GetTimestamp();
    for (var i = 0; i < 5; i++) await Cycle(-100 - i);
    report["WarmupMs"] = Stopwatch.GetElapsedTime(warm).TotalMilliseconds;
    report["Settings"] = JsonDocument.Parse(await Workload.Scalar<string>(admin, """
        SELECT json_object_agg(name,setting)::text FROM pg_settings WHERE name = ANY(ARRAY[
        'server_version','fsync','synchronous_commit','full_page_writes','wal_level','max_wal_senders','archive_mode',
        'jit','autovacuum','checkpoint_timeout','max_wal_size','shared_buffers','max_connections','data_checksums',
        'session_replication_role','default_transaction_isolation','log_statement','log_min_duration_statement'])
        """)).RootElement.Clone();
    async Task<JsonElement> Stats()
    {
        await Workload.Execute(admin, "SELECT pg_stat_clear_snapshot()");
        return JsonDocument.Parse(await Workload.Scalar<string>(admin, """
            SELECT json_build_object('wal',(SELECT row_to_json(w) FROM pg_stat_wal w),
              'bgwriter',(SELECT row_to_json(b) FROM pg_stat_bgwriter b),
              'tables',(SELECT json_agg(t) FROM (SELECT schemaname,relname,n_live_tup,n_dead_tup,vacuum_count,autovacuum_count,analyze_count,autoanalyze_count FROM pg_stat_all_tables WHERE schemaname NOT IN ('pg_catalog','information_schema')) t),
              'database_bytes',pg_database_size(current_database()),'jit_available',pg_jit_available())::text
            """)).RootElement.Clone();
    }
    // Flush warmup statistics before the measured interval. These SQL commands are diagnostics.
    async Task FlushAppStats()
    {
        await using var a = await appSource.OpenConnectionAsync();
        await using var b = await appSource.OpenConnectionAsync();
        await using var c = await appSource.OpenConnectionAsync();
        foreach (var connection in new[] { a, b, c, admin }) await Workload.Execute(connection, "SELECT pg_stat_force_next_flush()");
    }
    await FlushAppStats();
    report["Before"] = await Stats();
    var suite = Stopwatch.GetTimestamp();
    for (var i = 0; i < count; i++) samples.Add(await Cycle(i));
    report["SuiteMs"] = Stopwatch.GetElapsedTime(suite).TotalMilliseconds;
    await FlushAppStats();
    report["After"] = await Stats();
    var finalAudit = Stopwatch.GetTimestamp();
    await CheckRestored();
    report["FinalAuditMs"] = Stopwatch.GetElapsedTime(finalAudit).TotalMilliseconds;
    report["Succeeded"] = true;
}
catch (Exception e) { report["Failure"] = e.ToString(); }
report["Samples"] = samples;
report["ProcessMs"] = timer.Elapsed.TotalMilliseconds;
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { contender, count, succeeded = report["Succeeded"], suiteMs = report.GetValueOrDefault("SuiteMs"), failure = report.GetValueOrDefault("Failure") }));
return (bool)report["Succeeded"]! ? 0 : 1;

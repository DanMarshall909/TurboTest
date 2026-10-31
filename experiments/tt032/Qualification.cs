using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompetitiveBaseline;

static class Qualification
{
    public static async Task<int> Run(string name,string output,int lifetime,string configuration="durable")
    {
        Directory.CreateDirectory(output);var adapter=Runner.Adapter(name,output,lifetime);var probes=new List<object>();
        var failures=new List<string>();var binding=Runner.Binding();bool resetFaultInjected=false;
        object? initialResources=null;
        async Task Probe(string id,Func<Task> action)
        {
            Execution.Stop.Token.ThrowIfCancellationRequested();
            var start=Clock.Now();try{await action();probes.Add(new{Id=id,Passed=true,ElapsedMs=Clock.Ms(start)});Console.WriteLine($"{name}: {id} PASS");}
            catch(Exception e){probes.Add(new{Id=id,Passed=false,ElapsedMs=Clock.Ms(start),Error=e.ToString()});throw;}
        }
        async Task Restored(){await adapter.Settle();await Runner.VerifyReadyDatabases(adapter);}
        try
        {
            await adapter.Start();
            await adapter.Host.Configure(configuration);initialResources=await adapter.Host.Snapshot();
            await Probe("fresh-migration-and-seed",async()=>{Workload.Require(Workload.MigrationCalls==1&&Workload.SeedCalls==1,"ConstructionCount");await Restored();});
            await Probe("idempotent-migration",async()=>{
                await using(var l=await adapter.Acquire()){var ctx=l.Context();await Workload.Migrate(ctx);await Workload.CheckBaseline(await Workload.Open(ctx));}
                await Restored();
            });
            await Probe("concurrent-isolation-repeated-and-reordered",async()=>{
                var samples=new ConcurrentBag<Runner.Sample>();var active=new ConcurrentDictionary<string,int>();
                foreach(var order in new[]{new[]{0,1,2,3},new[]{3,2,1,0},new[]{0,0,0,0}})
                {await Task.WhenAll(order.Select(i=>Runner.Cycle(adapter,i,samples,active,default)));await Restored();}
                Workload.Require(samples.Count==12&&Workload.MigrationCalls==2&&Workload.SeedCalls==1,"CachedConstruction");
                probes.Add(new{Id="reordered-raw-cycles",Samples=samples.ToArray()});
            });
            foreach(var fault in new[]{"seed","sequence","workload"})
            await Probe("oracle-rejects-"+fault,async()=>{
                await using(var l=await adapter.Acquire())
                {
                    var a=await Workload.Open(l.Context());string expected;
                    if(fault=="seed"){await Workload.Execute(a,"UPDATE \"Parents\" SET \"Priority\"=-1 WHERE \"Id\"=1");expected="SeedState";}
                    else if(fault=="sequence"){await Workload.Execute(a,"SELECT setval('\"Parents_Id_seq\"',7,true)");expected="SequenceState";}
                    else {var b=await Workload.Open(l.Context());await Workload.Mutate(a,b,"probe");await Workload.Execute(a,"DELETE FROM \"Notes\" WHERE \"Id\"=3001");expected="NoteWork";}
                    try{if(fault=="workload")await Workload.CheckMutated(a,"probe");else await Workload.CheckBaseline(a);throw new InvalidDataException("OracleAcceptedFault");}
                    catch(InvalidDataException e) when(e.Message==expected){probes.Add(new{Id="fault-diagnostic",Fault=fault,Diagnostic=e.Message});}
                }
                await Restored();
            });
            await Probe("failed-test-restores-committed-writes",async()=>{
                try
                {
                    await using var l=await adapter.Acquire();var a=await Workload.Open(l.Context());var b=await Workload.Open(l.Context());
                    await Workload.Mutate(a,b,"failed");await Workload.CheckMutated(a,"failed");throw new InvalidOperationException("DeliberateTestFailure");
                }catch(InvalidOperationException e) when(e.Message=="DeliberateTestFailure"){}
                await Restored();
            });
            await Probe("cancelled-admission-does-not-consume-capacity",async()=>{
                var held=new List<Lease>();
                try
                {
                    var physical=new List<object>();var names=new HashSet<string>();
                    for(var i=0;i<4;i++)
                    {
                        var l=await adapter.Acquire();held.Add(l);
                        for(var j=0;j<3;j++)
                        {
                            var c=await Workload.Open(l.Context());names.Add(c.Database);
                            physical.Add(new{Database=c.Database,Pid=await Workload.Scalar<int>(c,"SELECT pg_backend_pid()")});
                        }
                    }
                    Workload.Require(names.Count==4,"HeldIsolation");
                    await using(var admin=await adapter.Host.Admin!.OpenConnectionAsync())
                    {
                        var inventory=await Workload.Scalar<string>(admin,"SELECT json_agg(json_build_object('database',datname,'sessions',n))::text FROM (SELECT datname,count(*) n FROM pg_stat_activity WHERE backend_type='client backend' GROUP BY datname) s");
                        var rows=JsonSerializer.Deserialize<JsonElement>(inventory);var total=0;var service=0;
                        foreach(var row in rows.EnumerateArray())
                        {
                            var n=row.GetProperty("sessions").GetInt32();total+=n;
                            if(names.Contains(row.GetProperty("database").GetString()!))Workload.Require(n<=4,"WorkloadConnectionCeiling");
                            else service+=n;
                        }
                        Workload.Require(total<=21,"CombinedConnectionCeiling");
                        Workload.Require(service<=5,"ServiceAndObserverConnectionCeiling");
                        probes.Add(new{Id="held-resource-inventory",PhysicalConnections=physical,Inventory=rows,TotalSessions=total,ServiceAndObserverSessions=service,WorkloadSlots=names.Count});
                    }
                    using var cts=new CancellationTokenSource(50);
                    try{await using var unexpected=await adapter.Acquire(cts.Token);throw new InvalidDataException("FifthLeaseAdmitted");}
                    catch(OperationCanceledException) when(cts.IsCancellationRequested){}
                }
                finally{foreach(var l in held)await l.DisposeAsync();}
                await Restored();
            });
            await Probe("cancelled-sql-settles-and-restores",async()=>{
                await using(var l=await adapter.Acquire())
                {
                    var c=await Workload.Open(l.Context());await Workload.Execute(c,"UPDATE \"Parents\" SET \"Priority\"=-2 WHERE \"Id\"=1");
                    using var cts=new CancellationTokenSource(50);
                    try{await Workload.Execute(c,"SELECT pg_sleep(30)",cts.Token);throw new InvalidDataException("SleepNotCancelled");}
                    catch(OperationCanceledException) when(cts.IsCancellationRequested){}
                }
                await Restored();
            });
            await Probe("schema-replacement-and-stale-context",async()=>{
                var l=await adapter.Acquire();var context=l.Context();var c=await Workload.Open(context);
                var oldIdentity=await Workload.Scalar<string>(c,"SELECT current_database()||':'||oid FROM pg_database WHERE datname=current_database()");
                var oldPid=await Workload.Scalar<int>(c,"SELECT pg_backend_pid()");
                await Workload.Execute(c,"ALTER TABLE \"Parents\" ADD COLUMN unexpected integer");l.InvalidateSchema();
                await l.DisposeAsync();await Restored();
                try{await context.Database.OpenConnectionAsync();throw new InvalidDataException("StaleContextUsable");}catch(ObjectDisposedException){}
                Workload.Require(c.State==System.Data.ConnectionState.Closed,"StalePhysicalConnectionOpen");
                try{await Workload.Scalar<int>(c,"SELECT 1");throw new InvalidDataException("StalePhysicalConnectionUsable");}
                catch(InvalidOperationException){}
                await using var admin=await adapter.Host.Admin!.OpenConnectionAsync();var oldOid=uint.Parse(oldIdentity.Split(':')[1]);
                await using var cmd=new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE oid=@oid",admin);cmd.Parameters.AddWithValue("oid",NpgsqlTypes.NpgsqlDbType.Oid,oldOid);
                Workload.Require((long)(await cmd.ExecuteScalarAsync())! == 0,"DatabaseNotReplaced");
                Workload.Require(await Workload.Scalar<long>(admin,$"SELECT count(*) FROM pg_stat_activity WHERE pid={oldPid}")==0,"StaleBackendSurvivedReplacement");
                probes.Add(new{Id="replacement-identity",Old=oldIdentity,OldPid=oldPid,OldConnectionClosed=true,OldBackendAbsent=true,CallerInvalidationRequired=name is "respawn" or "compiled"});
            });
            if(name is "respawn" or "compiled")
            await Probe("bounded-workload-connections-are-reused",async()=>{
                async Task<HashSet<int>> Pids()
                {
                    var leases=new List<Lease>();var pids=new HashSet<int>();
                    try
                    {
                        for(var i=0;i<Tuning.Capacity;i++)
                        {
                            var lease=await adapter.Acquire();leases.Add(lease);
                            for(var j=0;j<3;j++)pids.Add(await Workload.Scalar<int>(await Workload.Open(lease.Context()),"SELECT pg_backend_pid()"));
                        }
                    }
                    finally{foreach(var lease in leases)await lease.DisposeAsync();}
                    await Restored();return pids;
                }
                var first=await Pids();var second=await Pids();
                Workload.Require(first.Count==12 && first.SetEquals(second),"WorkloadConnectionsNotReused");
                probes.Add(new{Id="reused-backend-identities",First=first.Order().ToArray(),Second=second.Order().ToArray()});
            });
            if(name=="compiled")
            await Probe("post-sequence-failure-quarantines-and-replaces",async()=>{
                var sql=(SqlAdapter)adapter;Workload.Require(sql.ResetFailures==0,"UnexpectedQualificationResetFailure");
                var failuresBefore=sql.ResetFailures;var replacementsBefore=sql.Replacements;
                var lease=await adapter.Acquire();var c=await Workload.Open(lease.Context());
                var oldDatabase=c.Database;
                var oldOid=await Workload.Scalar<uint>(c,"SELECT oid FROM pg_database WHERE datname=current_database()");
                var oldPid=await Workload.Scalar<int>(c,"SELECT pg_backend_pid()");
                await Workload.Execute(c,"UPDATE \"Parents\" SET \"Priority\"=-4 WHERE \"Id\"=1");
                sql.InjectSequenceFailure();await lease.DisposeAsync();await Restored();
                Workload.Require(sql.ResetFailures==failuresBefore+1 && sql.Replacements==replacementsBefore+1,"FailedResetNotReplaced");
                Workload.Require(sql.ObservedPoisonedSequence==9001,"SequenceHazardNotObserved");
                await using var admin=await adapter.Host.Admin!.OpenConnectionAsync();
                await using var cmd=new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE oid=@oid",admin);cmd.Parameters.AddWithValue("oid",NpgsqlTypes.NpgsqlDbType.Oid,oldOid);
                Workload.Require((long)(await cmd.ExecuteScalarAsync())! == 0,"FailedResetDatabaseReused");
                Workload.Require(await Workload.Scalar<long>(admin,$"SELECT count(*) FROM pg_stat_activity WHERE pid={oldPid}")==0,"FailedResetBackendSurvived");
                var samples=new ConcurrentBag<Runner.Sample>();var active=new ConcurrentDictionary<string,int>();
                await Task.WhenAll(Enumerable.Range(0,4).Select(i=>Runner.Cycle(adapter,i,samples,active,default)));await Restored();
                Workload.Require(samples.Count==4 && sql.ResetFailures==failuresBefore+1,"RecoveryWorkFailed");
                Workload.Require(samples.Any(s=>s.Database==oldDatabase && s.Oid!=oldOid),"ReplacementSlotNotExercised");
                probes.Add(new{Id="sequence-hazard-recovery",OldOid=oldOid,OldPid=oldPid,sql.ObservedPoisonedSequence,State=sql.State()});
            });
            await Probe("reset-failure-characterized-and-owned-resources-settle",async()=>{
                var l=await adapter.Acquire();var a=await Workload.Open(l.Context());await Workload.Execute(a,"UPDATE \"Parents\" SET \"Priority\"=-3 WHERE \"Id\"=1");
                resetFaultInjected=true;
                await Host.Docker.Containers.StopContainerAsync(adapter.Host.PgId,new(){WaitBeforeKillSeconds=1});
                Exception? observed=null;
                try{await l.DisposeAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));await adapter.Settle(deadline.Token);}
                catch(Exception e){observed=e;}
                probes.Add(new{Id="reset-failure-observation",Error=observed?.ToString(),ObservationCancellation=observed is OperationCanceledException});
                var state=JsonSerializer.Serialize(adapter.State());
                if(observed is OperationCanceledException)
                {
                    Workload.Require(name=="integresql" && (state.Contains("shutting down",StringComparison.OrdinalIgnoreCase)||state.Contains("connection refused",StringComparison.OrdinalIgnoreCase)||state.Contains("connection reset",StringComparison.OrdinalIgnoreCase)),"TimeoutWithoutUnderlyingFailureEvidence");
                    probes.Add(new{Id="reset-failure-limitation",ApiFailureReceipt=false,ObservationTimeout=true,ControllerState=JsonSerializer.Deserialize<JsonElement>(state),Diagnostic="Server failure is logged; acquisition/restoration has no failed-return receipt. Caller needs a deadline and fixture replacement."});
                }
                else Workload.Require(observed is not null && BoundaryFailure(observed),"UnexpectedResetFailureEvidence");
                // The owned server is deliberately unavailable. Force cleanup is a declared recovery path,
                // not a successful reset. Subsequent test runs construct a new fixture.
                await adapter.Host.DisposeAsync();
            });
        }
        catch(Exception e){failures.Add(e.ToString());}
        finally
        {
            try{await adapter.DisposeAsync();}
            catch(Exception e){if(resetFaultInjected && BoundaryFailure(e))probes.Add(new{Id="expected-boundary-failure-during-drain",Error=e.ToString()});else failures.Add(e.ToString());}
            try{await adapter.Host.VerifyAbsent();probes.Add(new{Id="independent-final-resource-absence",Passed=true,Containers=adapter.Host.Ids,Volumes=adapter.Host.Volumes});}
            catch(Exception e){failures.Add(e.ToString());}
        }
        if(failures.Count==0)
        {
            var recovery=await Runner.Run(name,4,2,"fresh",Path.Combine(output,"fresh-recovery"),lifetime,configuration);
            probes.Add(new{Id="fresh-fixture-recovers-after-server-failure",Passed=recovery==0});
            if(recovery!=0)failures.Add("Fresh fixture recovery failed; see fresh-recovery/report.json");
        }
        var report=new{Contender=name,Configuration=configuration,Capacity=Tuning.Capacity,ResetWorkers=Tuning.Workers,Binding=binding,InitialResources=initialResources,Probes=probes,Failures=failures,Cleanup=adapter.Host.Cleanup,Succeeded=failures.Count==0,EndedUtc=DateTime.UtcNow};
        await File.WriteAllTextAsync(Path.Combine(output,"qualification.json"),JsonSerializer.Serialize(report,Runner.Json));
        Console.WriteLine($"{name}: qualification {(failures.Count==0?"PASS":"FAIL")}");return failures.Count==0?0:1;
    }
    static bool BoundaryFailure(Exception e)=>e switch
    {
        NpgsqlException or HttpRequestException=>true,
        TurboTest.EFCore.TtLifecycleException lifecycle=>
            lifecycle.Stage is "BaselineRowReset" or "PreparedPoolDispose" &&
            (lifecycle.SqlState is "57P01" or "57P02" or "08001" or "08006" ||
             lifecycle.Message.Contains("NpgsqlException",StringComparison.Ordinal) &&
             System.Text.RegularExpressions.Regex.Matches(lifecycle.Message,@"\b[A-Za-z]+Exception\b")
                 .All(m=>m.Value is "NpgsqlException" or "AggregateException")),
        AggregateException aggregate=>aggregate.InnerExceptions.Count>0 && aggregate.InnerExceptions.All(BoundaryFailure),
        _=>e.InnerException is not null && BoundaryFailure(e.InnerException)
    };
}

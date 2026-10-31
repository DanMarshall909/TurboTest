using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompetitiveBaseline;

static class Qualification
{
    public static async Task<int> Run(string name,string output,int lifetime)
    {
        Directory.CreateDirectory(output);var adapter=Runner.Adapter(name,output,lifetime);var probes=new List<object>();
        var failures=new List<string>();var binding=Runner.Binding();bool resetFaultInjected=false;
        async Task Probe(string id,Func<Task> action)
        {
            var start=Clock.Now();try{await action();probes.Add(new{Id=id,Passed=true,ElapsedMs=Clock.Ms(start)});Console.WriteLine($"{name}: {id} PASS");}
            catch(Exception e){probes.Add(new{Id=id,Passed=false,ElapsedMs=Clock.Ms(start),Error=e.ToString()});throw;}
        }
        async Task Restored(){await adapter.Settle();await Runner.VerifyReadyDatabases(adapter.Host);}
        try
        {
            await adapter.Start();
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
                    for(var i=0;i<4;i++){var l=await adapter.Acquire();held.Add(l);await Workload.Open(l.Context());}
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
                await Workload.Execute(c,"ALTER TABLE \"Parents\" ADD COLUMN unexpected integer");l.InvalidateSchema();
                await l.DisposeAsync();await Restored();
                try{await context.Database.OpenConnectionAsync();throw new InvalidDataException("StaleContextUsable");}catch(ObjectDisposedException){}
                await using var admin=await adapter.Host.Admin!.OpenConnectionAsync();var oldOid=uint.Parse(oldIdentity.Split(':')[1]);
                await using var cmd=new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE oid=@oid",admin);cmd.Parameters.AddWithValue("oid",NpgsqlTypes.NpgsqlDbType.Oid,oldOid);
                Workload.Require((long)(await cmd.ExecuteScalarAsync())! == 0,"DatabaseNotReplaced");
                probes.Add(new{Id="replacement-identity",Old=oldIdentity,CallerInvalidationRequired=name=="respawn"});
            });
            await Probe("reset-failure-is-visible-and-owned-resources-settle",async()=>{
                var l=await adapter.Acquire();var a=await Workload.Open(l.Context());await Workload.Execute(a,"UPDATE \"Parents\" SET \"Priority\"=-3 WHERE \"Id\"=1");
                resetFaultInjected=true;
                await Host.Docker.Containers.StopContainerAsync(adapter.Host.PgId,new(){WaitBeforeKillSeconds=1});
                Exception? observed=null;
                try{await l.DisposeAsync();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(3));await adapter.Settle(deadline.Token);}
                catch(Exception e){observed=e;}
                Workload.Require(observed is not null,"ResetFailureHidden");
                probes.Add(new{Id="reset-failure-observation",Error=observed!.ToString(),ObservationCancellation=observed is OperationCanceledException});
                // The owned server is deliberately unavailable. Force cleanup is a declared recovery path,
                // not a successful reset. Subsequent test runs construct a new fixture.
                await adapter.Host.DisposeAsync();
            });
        }
        catch(Exception e){failures.Add(e.ToString());}
        finally
        {
            try{await adapter.DisposeAsync();}
            catch(Exception e){if(resetFaultInjected)probes.Add(new{Id="expected-drain-after-server-failure",Error=e.ToString()});else failures.Add(e.ToString());}
        }
        if(failures.Count==0)
        {
            var recovery=await Runner.Run(name,4,2,"fresh",Path.Combine(output,"fresh-recovery"),lifetime,"durable");
            probes.Add(new{Id="fresh-fixture-recovers-after-server-failure",Passed=recovery==0});
            if(recovery!=0)failures.Add("Fresh fixture recovery failed; see fresh-recovery/report.json");
        }
        var report=new{Contender=name,Binding=binding,Probes=probes,Failures=failures,Cleanup=adapter.Host.Cleanup,Succeeded=failures.Count==0,EndedUtc=DateTime.UtcNow};
        await File.WriteAllTextAsync(Path.Combine(output,"qualification.json"),JsonSerializer.Serialize(report,Runner.Json));
        Console.WriteLine($"{name}: qualification {(failures.Count==0?"PASS":"FAIL")}");return failures.Count==0?0:1;
    }
}

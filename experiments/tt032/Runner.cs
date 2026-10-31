using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompetitiveBaseline;

static class Runner
{
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static IAdapter Adapter(string name,string output,int lifetime)=>name switch{
        "turbotest"=>new TurboAdapter(),"respawn"=>new SqlAdapter(true),"native-template"=>new SqlAdapter(false),"integresql"=>new IntegreAdapter(output,lifetime),_=>throw new ArgumentException("Unknown contender")};
    public static object Binding()=>new{
        Runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OS=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        AssemblySha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),
        TurboSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TurboTest.EFCore.Tt).Assembly.Location))),
        Inputs=Directory.EnumerateFiles("experiments/tt032","*",SearchOption.TopDirectoryOnly).Order().ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))
    };
    public static async Task<int> Run(string name,int count,int workers,string mode,string output,int lifetime)
    {
        Directory.CreateDirectory(output);
        var report=new Dictionary<string,object?>{{"Contender",name},{"Count",count},{"Workers",workers},{"Mode",mode},{"Binding",Binding()},{"StartedUtc",DateTime.UtcNow},{"Succeeded",false}};
        var failures=new List<object>();var samples=new ConcurrentBag<Sample>();var active=new ConcurrentDictionary<string,int>();
        var adapter=Adapter(name,output,lifetime);var overall=Clock.Now();
        using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try
        {
            var start=Clock.Now();await adapter.Start();report["PreparationMs"]=Clock.Ms(start);
            report["InitialResources"]=await adapter.Host.Snapshot();report["InitialState"]=adapter.State();
            var pre=Clock.Now();
            if(mode=="warm") {for(var i=0;i<4;i++)await Cycle(adapter,-i-1,new(),active,deadline.Token);await adapter.Settle(deadline.Token);}
            report["WarmupMs"]=mode=="warm"?Clock.Ms(pre):0;
            report["ConstructionBefore"]=new{Workload.MigrationCalls,Workload.SeedCalls};
            var suite=Clock.Now();
            await Task.WhenAll(Enumerable.Range(0,workers).Select(async w=>{
                for(var i=w;i<count;i+=workers)await Cycle(adapter,i,samples,active,deadline.Token);
            }));
            report["TestsReturnedMs"]=Clock.Ms(suite);
            var settle=Clock.Now();await adapter.Settle(deadline.Token);report["SettlementMs"]=Clock.Ms(settle);
            report["SuiteMs"]=Clock.Ms(suite);report["SettledState"]=adapter.State();
            report["FinalResources"]=await adapter.Host.Snapshot();
            var verify=Clock.Now();report["RestoredDatabases"]=await VerifyReadyDatabases(adapter.Host,deadline.Token);report["FinalAuditMs"]=Clock.Ms(verify);
            report["ConstructionAfter"]=new{Workload.MigrationCalls,Workload.SeedCalls};
            Workload.Require(samples.Count==count,"CompleteSamples");report["Succeeded"]=true;
        }
        catch(Exception e){failures.Add(new{Phase="Run",Type=e.GetType().FullName,e.Message,Detail=e.ToString()});}
        finally
        {
            var drain=Clock.Now();
            try{await adapter.DisposeAsync();report["CleanupCompleted"]=true;}
            catch(Exception e){report["Succeeded"]=false;report["CleanupCompleted"]=false;failures.Add(new{Phase="Drain",Type=e.GetType().FullName,e.Message,Detail=e.ToString()});}
            report["DrainMs"]=Clock.Ms(drain);report["ObserverInclusiveWallMs"]=Clock.Ms(overall);
            report["Cleanup"]=adapter.Host.Cleanup;report["Failures"]=failures;report["Samples"]=samples.OrderBy(s=>s.Ordinal).ToArray();report["EndedUtc"]=DateTime.UtcNow;
            if(report.TryGetValue("SuiteMs",out var suite)){
                var prep=(double)report["PreparationMs"]!;var d=(double)report["DrainMs"]!;
                report["FeedbackMs"]=(double)suite!+(mode=="fresh"?prep:0);
                report["CompleteMs"]=(double)suite!+prep+(double)report["WarmupMs"]!+d;
                report["UsefulThroughputPerSecond"]=count*1000/(double)suite!;
            }
            await File.WriteAllTextAsync(Path.Combine(output,"report.json"),JsonSerializer.Serialize(report,Json));
        }
        Console.WriteLine(JsonSerializer.Serialize(new{name,count,workers,mode,Succeeded=report["Succeeded"],Samples=samples.Count,SuiteMs=report.GetValueOrDefault("SuiteMs"),Failures=failures}));
        return (bool)report["Succeeded"]!?0:1;
    }
    public sealed record Sample(int Ordinal,string Database,uint Oid,int Pid1,int Pid2,double AcquireMs,double FirstSqlMs,double ApplicationMs,double ReturnMs,double CycleMs);
    public static async Task Cycle(IAdapter adapter,int ordinal,ConcurrentBag<Sample> samples,ConcurrentDictionary<string,int> active,CancellationToken token)
    {
        var cycle=Clock.Now();var lease=await adapter.Acquire(token);var acquire=Clock.Ms(cycle);string? database=null;
        try
        {
            var first=Clock.Now();var a=await Workload.Open(lease.Context(),token);
            database=a.Database;
            var identity=await Workload.Scalar<string>(a,"SELECT current_database()||':'||(SELECT oid FROM pg_database WHERE datname=current_database())||':'||pg_backend_pid()",token);
            var firstSql=Clock.Ms(first);var parts=identity.Split(':');var oid=uint.Parse(parts[1]);var pid1=int.Parse(parts[2]);
            Workload.Require(active.TryAdd(database,ordinal),"ExclusiveDatabaseLease");
            var work=Clock.Now();await Workload.CheckBaseline(a,token);
            var b=await Workload.Open(lease.Context(),token);var pid2=await Workload.Scalar<int>(b,"SELECT pg_backend_pid()",token);Workload.Require(pid1!=pid2,"PhysicalConnections");
            var marker="case"+ordinal;await Workload.Mutate(a,b,marker,token);
            var c=await Workload.Open(lease.Context(),token);var pid3=await Workload.Scalar<int>(c,"SELECT pg_backend_pid()",token);Workload.Require(pid3!=pid1&&pid3!=pid2,"IndependentObserver");
            await Workload.CheckMutated(c,marker,token);var app=Clock.Ms(work);
            var ret=Clock.Now();await lease.DisposeAsync();var returned=Clock.Ms(ret);
            samples.Add(new(ordinal,database,oid,pid1,pid2,acquire,firstSql,app,returned,Clock.Ms(cycle)));
        }
        finally{if(database is not null)active.TryRemove(database,out _);await lease.DisposeAsync();}
    }
    public static async Task<string[]> VerifyReadyDatabases(Host host,CancellationToken token=default)
    {
        var names=new List<string>();
        await using(var c=await host.Admin!.OpenConnectionAsync(token))
        await using(var cmd=new NpgsqlCommand("SELECT datname FROM pg_database WHERE datallowconn AND datname NOT IN ('postgres','template0','template1') ORDER BY datname",c))
        await using(var r=await cmd.ExecuteReaderAsync(token)){while(await r.ReadAsync(token))names.Add(r.GetString(0));}
        var verified=new List<string>();
        foreach(var name in names)
        {
            await using var source=NpgsqlDataSource.Create(host.ForDatabase(name,admin:true));await using var c=await source.OpenConnectionAsync(token);
            var has=await Workload.Scalar<bool>(c,"SELECT to_regclass('public.\"Parents\"') IS NOT NULL",token);
            if(has){await Workload.CheckBaseline(c,token);verified.Add(name);}
        }
        Workload.Require(verified.Count>=4,"RestoredCapacity");return verified.ToArray();
    }
}

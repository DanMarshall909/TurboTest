using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using TurboTest.EFCore;

namespace CompetitiveBaseline;

sealed class Lease(Func<Context> create,Func<Task> release,Action? invalidate=null) : IAsyncDisposable
{
    readonly List<Context> contexts=[];
    bool returned;
    public Context Context(){ObjectDisposedException.ThrowIf(returned,this);var c=create();contexts.Add(c);return c;}
    public void InvalidateSchema()=>invalidate?.Invoke();
    public async ValueTask DisposeAsync()
    {
        if(returned)return;returned=true;
        try{foreach(var c in contexts)await c.DisposeAsync();}finally{await release();}
    }
}

interface IAdapter : IAsyncDisposable
{
    Host Host {get;}
    string Name {get;}
    Task Start();
    Task<Lease> Acquire(CancellationToken token=default);
    Task Settle(CancellationToken token=default);
    object State();
    bool IsWorkloadDatabase(string name);
}

sealed class TurboAdapter : IAdapter
{
    public Host Host {get;}=new();
    public string Name=>"turbotest";
    TtFactory<Context>? fixture;
    public async Task Start()
    {
        fixture=await Tt.CreateFactoryAsync(Workload.Baseline,o=>{
            o.PoolSize=Tuning.Capacity;o.StartupWarmDatabases=Tuning.Capacity;o.WarmFloor=0;o.EnableDynamicScaling=false;o.MaxContainers=1;
            o.PreparationConcurrency=Tuning.Workers;o.ResetConcurrency=Tuning.Workers;o.MaxConnectionsPerDatabase=4;o.MaxAdministratorConnections=4;
            o.MemoryLimitMb=256;o.ContainerPrefix=Host.Prefix;o.DockerImage=Host.Image;o.DockerHost="unix:///var/run/docker.sock";
            o.Username="postgres";o.Password="tt032-disposable";o.StartupTimeout=TimeSpan.FromMinutes(2);o.DrainTimeout=TimeSpan.FromSeconds(30);
        });
        var owned=await Host.Docker.Containers.ListContainersAsync(new(){All=true,Filters=new Dictionary<string,IDictionary<string,bool>>{{"name",new Dictionary<string,bool>{{Host.Prefix,true}}}}});
        Workload.Require(owned.Count==1,"TurboHostIdentity");Host.PgId=owned[0].ID;await Host.Capture(owned[0].ID);
        var i=await Host.Docker.Containers.InspectContainerAsync(Host.PgId);
        var env=i.Config.Env.Select(e=>e.Split('=',2)).ToDictionary(e=>e[0],e=>e[1]);
        Host.ConnectionString=new NpgsqlConnectionStringBuilder{Host="127.0.0.1",Port=int.Parse(i.NetworkSettings.Ports["5432/tcp"][0].HostPort),Username=env["POSTGRES_USER"],Password=env["POSTGRES_PASSWORD"],Database="postgres",MaxPoolSize=1,CommandTimeout=15,Enlist=false}.ConnectionString;
        Host.Admin=NpgsqlDataSource.Create(Host.ConnectionString);
    }
    public async Task<Lease> Acquire(CancellationToken token=default)
    {var l=await fixture!.CreateLeaseAsync(cancellationToken:token);return new(()=>l.CreateContext(),()=>l.DisposeAsync().AsTask());}
    public Task Settle(CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var s=fixture!.PoolState;
        Workload.Require(s.Ready==Tuning.Capacity && s.Leased==0 && s.Preparing==0 && s.ExecutingResets==0,"TurboCapacity");return Task.CompletedTask;
    }
    public object State()=>fixture!.PoolState;
    public bool IsWorkloadDatabase(string name)=>name.StartsWith("testdb_slot_",StringComparison.Ordinal);
    public async ValueTask DisposeAsync(){try{if(fixture is not null)await fixture.DisposeAsync();}finally{await Host.DisposeAsync();}}
}

// Bounded channels and one reset worker let restoration overlap another test without hiding its cost.
sealed class SqlAdapter(bool respawn) : IAdapter
{
    public Host Host {get;}=new();
    public string Name=>respawn?"respawn":"native-template";
    const string Template="tt032_template";
    readonly Channel<int> ready=Channel.CreateBounded<int>(Tuning.Capacity);
    readonly NpgsqlDataSource?[] sources=new NpgsqlDataSource?[Tuning.Capacity];
    readonly Respawner?[] resetters=new Respawner?[Tuning.Capacity];
    readonly SemaphoreSlim reset=new(Tuning.Workers);
    readonly List<Task> jobs=[];
    readonly List<object> restoration=[];
    public int CompletedReturns;
    public async Task Start()
    {
        await Host.StartPostgres();
        await using(var a=await Host.Admin!.OpenConnectionAsync())await Workload.Execute(a,$"CREATE DATABASE {Template} OWNER tt032_app TEMPLATE template0");
        await using(var source=NpgsqlDataSource.Create(Host.ForDatabase(Template)))
        await using(var c=Workload.New(source)){await Workload.Migrate(c);await Workload.Seed(c);}
        await using(var a=await Host.Admin!.OpenConnectionAsync())await Workload.Execute(a,$"ALTER DATABASE {Template} ALLOW_CONNECTIONS false");
        using var preparation=new SemaphoreSlim(Tuning.Workers);
        await Task.WhenAll(Enumerable.Range(0,Tuning.Capacity).Select(async i=>{
            await preparation.WaitAsync();try{await Clone(i);await ready.Writer.WriteAsync(i);}finally{preparation.Release();}
        }));
    }
    async Task Clone(int i)
    {
        await using(var a=await Host.Admin!.OpenConnectionAsync())await Workload.Execute(a,$"CREATE DATABASE tt032_slot_{i} OWNER tt032_app TEMPLATE {Template} STRATEGY WAL_LOG");
        sources[i]=NpgsqlDataSource.Create(Host.ForDatabase($"tt032_slot_{i}"));
        if(respawn){await using var c=await sources[i]!.OpenConnectionAsync();resetters[i]=await Respawner.CreateAsync(c,new(){DbAdapter=DbAdapter.Postgres,SchemasToInclude=["public"],TablesToIgnore=[new Table("SchemaVersion")],CommandTimeout=15});}
    }
    public async Task<Lease> Acquire(CancellationToken token=default)
    {
        var i=await ready.Reader.ReadAsync(token);
        var replace=false;
        return new(()=>Workload.New(sources[i]!),()=>{
            lock(jobs)jobs.Add(Restore(i,replace));return Task.CompletedTask;
        },()=>replace=true);
    }
    async Task Restore(int i,bool replace)
    {
        var requested=Clock.Now();
        await reset.WaitAsync();
        var executing=Clock.Now();
        double disconnect=0,drop=0,clone=0;
        try
        {
            if(respawn && !replace)
            {
                await using var c=await sources[i]!.OpenConnectionAsync();
                await resetters[i]!.ResetAsync(c);await Workload.Execute(c,Workload.SeedSql);
            }
            else
            {
                var phase=Clock.Now();await sources[i]!.DisposeAsync();sources[i]=null;
                await using(var a=await Host.Admin!.OpenConnectionAsync())
                {
                    // Npgsql disposal sends close, but the server can still be processing it.
                    // Avoid FORCE's backend termination wait on the ordinary already-closed path.
                    var until=DateTime.UtcNow.AddSeconds(2);
                    while(await Workload.Scalar<long>(a,$"SELECT count(*) FROM pg_stat_activity WHERE datname='tt032_slot_{i}'")!=0 && DateTime.UtcNow<until)await Task.Delay(1);
                    disconnect=Clock.Ms(phase);phase=Clock.Now();
                    await Workload.Execute(a,$"DROP DATABASE tt032_slot_{i} WITH (FORCE)");drop=Clock.Ms(phase);
                }
                phase=Clock.Now();await Clone(i);clone=Clock.Ms(phase);
            }
            lock(restoration)restoration.Add(new{Slot=i,QueueMs=Clock.Ms(requested)-Clock.Ms(executing),ExecutionMs=Clock.Ms(executing),DisconnectMs=disconnect,DropMs=drop,CloneMs=clone});
            Interlocked.Increment(ref CompletedReturns);await ready.Writer.WriteAsync(i);
        }
        catch(Exception e){ready.Writer.TryComplete(e);throw;}
        finally{reset.Release();}
    }
    public async Task Settle(CancellationToken token=default)
    {Task[] tasks;lock(jobs)tasks=jobs.ToArray();await Task.WhenAll(tasks).WaitAsync(token);Workload.Require(ready.Reader.Count==Tuning.Capacity,"SqlCapacity");}
    public object State()=>new{Ready=ready.Reader.Count,CompletedReturns,ResetConcurrency=Tuning.Workers,Restoration=restoration.ToArray()};
    public bool IsWorkloadDatabase(string name)=>name.StartsWith("tt032_slot_",StringComparison.Ordinal);
    public async ValueTask DisposeAsync()
    {
        try{Task[] tasks;lock(jobs)tasks=jobs.ToArray();await Task.WhenAll(tasks);}
        finally{foreach(var s in sources)if(s is not null)await s.DisposeAsync();await Host.DisposeAsync();reset.Dispose();}
    }
}

sealed class IntegreAdapter(string output,int lifetime=25) : IAdapter
{
    public Host Host {get;}=new();
    public string Name=>"integresql";
    readonly string hash="tt032"+Guid.NewGuid().ToString("N");
    HttpClient? http;
    ControllerLogs? logs;
    readonly SemaphoreSlim admission=new(Tuning.Capacity);
    int issued;
    public async Task Start()
    {
        await Host.StartPostgres();var apiPort=Host.FreePort();var pg=new NpgsqlConnectionStringBuilder(Host.ConnectionString);
        Host.ControllerId=await Host.Create(Host.ControllerImage,"_controller",[
            "INTEGRESQL_ADDRESS=127.0.0.1",$"INTEGRESQL_PORT={apiPort}","INTEGRESQL_PGHOST=127.0.0.1",$"INTEGRESQL_PGPORT={pg.Port}",
            "INTEGRESQL_PGUSER=postgres","INTEGRESQL_PGPASSWORD=tt032-disposable","INTEGRESQL_TEST_PGUSER=tt032_app","INTEGRESQL_TEST_PGPASSWORD=tt032-disposable",
            $"INTEGRESQL_TEST_INITIAL_POOL_SIZE={Tuning.Capacity}",$"INTEGRESQL_TEST_MAX_POOL_SIZE={Tuning.Capacity}",$"INTEGRESQL_POOL_MAX_PARALLEL_TASKS={Tuning.Workers}",
            $"INTEGRESQL_TEST_DB_MINIMAL_LIFETIME_MS={lifetime}","INTEGRESQL_TEST_DB_RETRY_RECREATE_SLEEP_MIN_MS=5","INTEGRESQL_TEST_DB_RETRY_RECREATE_SLEEP_MAX_MS=25",
            "INTEGRESQL_LOGGER_LEVEL=trace","INTEGRESQL_LOGGER_REQUEST_LEVEL=info","INTEGRESQL_LOGGER_PRETTY_PRINT_CONSOLE=false"],new(){NetworkMode="host",Memory=64L*1024*1024});
        logs=new(Host.ControllerId,Path.Combine(output,"integresql.jsonl"));
        http=new(){BaseAddress=new Uri($"http://127.0.0.1:{apiPort}/api/v1/"),Timeout=TimeSpan.FromSeconds(60)};
        var until=DateTime.UtcNow.AddSeconds(30);
        while(true){try{using var r=await http.GetAsync("health");break;}catch(HttpRequestException) when(DateTime.UtcNow<until){await Task.Delay(50);}}
        using var init=await http.PostAsJsonAsync("templates",new{hash});init.EnsureSuccessStatusCode();
        var body=await init.Content.ReadFromJsonAsync<JsonElement>();var db=body.GetProperty("database").GetProperty("config").GetProperty("database").GetString()!;
        await using(var a=await Host.Admin!.OpenConnectionAsync())await Workload.Execute(a,$"ALTER DATABASE {db} OWNER TO tt032_app");
        await using(var source=NpgsqlDataSource.Create(Host.ForDatabase(db)))
        await using(var c=Workload.New(source)){await Workload.Migrate(c);await Workload.Seed(c);}
        using var done=await http.PutAsync($"templates/{hash}",null);done.EnsureSuccessStatusCode();
        await Settle();
    }
    public async Task<Lease> Acquire(CancellationToken token=default)
    {
        await admission.WaitAsync(token);
        try
        {
            using var r=await http!.GetAsync($"templates/{hash}/tests",token);r.EnsureSuccessStatusCode();
            Interlocked.Increment(ref issued);
            var body=await r.Content.ReadFromJsonAsync<JsonElement>(token);var db=body.GetProperty("database").GetProperty("config").GetProperty("database").GetString()!;
            var source=NpgsqlDataSource.Create(Host.ForDatabase(db));
            // Keep one real session alive from admission through release: auto-clean infers ownership from sessions.
            NpgsqlConnection pin;
            try{pin=await source.OpenConnectionAsync(token);}catch{await source.DisposeAsync();throw;}
            return new(()=>Workload.New(source),async()=>{try{await pin.DisposeAsync();await source.DisposeAsync();}finally{admission.Release();}});
        }catch{admission.Release();throw;}
    }
    public async Task Settle(CancellationToken token=default)
    {
        var until=DateTime.UtcNow.AddSeconds(60);
        while(logs is null || Volatile.Read(ref logs.Ready)!=Tuning.Capacity || logs.Dirty!=0 || logs.Acquisitions<issued || logs.Recreations<Tuning.Capacity+issued)
        {token.ThrowIfCancellationRequested();if(DateTime.UtcNow>until)throw new TimeoutException("IntegreCapacity");await Task.Delay(2,token);}
        // 'ready' is emitted after CREATE finishes and queue publication, before the worker's final bookkeeping.
        // Confirm no database-changing SQL remains; this SQL observation follows publication.
        await using var a=await Host.Admin!.OpenConnectionAsync(token);
        while(await Workload.Scalar<long>(a,"SELECT count(*) FROM pg_stat_activity WHERE pid<>pg_backend_pid() AND state='active' AND backend_type='client backend'",token)!=0)
        {if(DateTime.UtcNow>until)throw new TimeoutException("IntegreBackgroundSettlement");await Task.Delay(2,token);}
    }
    public object State()=>new{Ready=logs?.Ready,Dirty=logs?.Dirty,RecreatingAtLastLog=logs?.Recreating,DatabaseCount=logs?.DatabaseCount,ObservedAcquisitions=logs?.Acquisitions,CompletedRecreations=logs?.Recreations,Issued=issued,LifetimeMs=lifetime,ControllerErrors=logs?.Errors.ToArray()};
    public bool IsWorkloadDatabase(string name)=>name.StartsWith("integresql_test_"+hash+"_",StringComparison.Ordinal);
    public async ValueTask DisposeAsync()
    {
        try{if(http is not null){using var r=await http.DeleteAsync($"templates/{hash}");r.EnsureSuccessStatusCode();}}
        finally{try{await Host.DisposeAsync();}finally{if(logs is not null)await logs.DisposeAsync();http?.Dispose();admission.Dispose();}}
    }
}

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using Npgsql;

namespace CompetitiveBaseline;

static class Tuning
{
    public static int Workers {get;}=int.Parse(Environment.GetEnvironmentVariable("TT032_RESET_WORKERS")??"1");
    public static int Capacity {get;}=int.Parse(Environment.GetEnvironmentVariable("TT032_CAPACITY")??"4");
}

static class Execution { public static CancellationTokenSource Stop {get;}=new(); }

static class Clock
{
    public static long Now()=>Stopwatch.GetTimestamp();
    public static double Ms(long start)=>Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}

sealed class Host : IAsyncDisposable
{
    public static readonly DockerClient Docker=new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    public const string Image="postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea";
    public const string ControllerImage="ghcr.io/allaboutapps/integresql@sha256:66b7433399f1907bad9e4b7cc7bd528ed4ebfccddecb8fd65da4af4d06a68c5a";
    public string Prefix { get; }="tt032_"+Guid.NewGuid().ToString("N");
    public List<string> Ids {get;}=[];
    public List<string> Volumes {get;}=[];
    public List<object> Cleanup {get;}=[];
    public string? PgId {get;set;}
    public string? ControllerId {get;set;}
    public NpgsqlDataSource? Admin {get;set;}
    public string ConnectionString {get;set;}="";
    public async Task Configure(string configuration)
    {
        if(configuration=="durable")return;
        Workload.Require(configuration=="relaxed","DurabilityConfiguration");
        await using var c=await Admin!.OpenConnectionAsync();
        foreach(var setting in new[]{"fsync","synchronous_commit","full_page_writes"})await Workload.Execute(c,$"ALTER SYSTEM SET {setting}='off'");
        await Workload.Execute(c,"SELECT pg_reload_conf()");
        var until=DateTime.UtcNow.AddSeconds(5);
        while(await Workload.Scalar<string>(c,"SHOW fsync")!="off")
        {if(DateTime.UtcNow>until)throw new TimeoutException("SettingsReload");await Task.Delay(5);}
    }
    public async Task<string> Create(string image,string suffix,string[] env,HostConfig config)
    {
        var r=await Docker.Containers.CreateContainerAsync(new CreateContainerParameters {
            Image=image,Name=Prefix+suffix,Env=env,Labels=new Dictionary<string,string>{{"tt032.owner",Prefix}},
            ExposedPorts=new Dictionary<string,EmptyStruct>{{"5432/tcp",default}},HostConfig=config });
        Ids.Add(r.ID);
        await Docker.Containers.StartContainerAsync(r.ID,new());
        await Capture(r.ID); return r.ID;
    }
    public async Task Capture(string id)
    {
        if(!Ids.Contains(id)) Ids.Add(id);
        var i=await Docker.Containers.InspectContainerAsync(id);
        foreach(var m in i.Mounts.Where(m=>m.Type=="volume")) if(!Volumes.Contains(m.Name)) Volumes.Add(m.Name);
    }
    public async Task StartPostgres()
    {
        PgId=await Create(Image,"_pg",["POSTGRES_USER=postgres","POSTGRES_PASSWORD=tt032-disposable","POSTGRES_DB=postgres","POSTGRES_INITDB_ARGS=--no-sync"],
            new HostConfig{ Memory=256L*1024*1024,PortBindings=new Dictionary<string,IList<PortBinding>>{{"5432/tcp",new[]{new PortBinding{HostIP="127.0.0.1",HostPort=""}}}} });
        var i=await Docker.Containers.InspectContainerAsync(PgId);
        var port=int.Parse(i.NetworkSettings.Ports["5432/tcp"][0].HostPort);
        ConnectionString=new NpgsqlConnectionStringBuilder{Host="127.0.0.1",Port=port,Username="postgres",Password="tt032-disposable",Database="postgres",Pooling=true,MaxPoolSize=4,Timeout=5,CommandTimeout=15,Enlist=false}.ConnectionString;
        Admin=NpgsqlDataSource.Create(ConnectionString);
        var until=DateTime.UtcNow.AddSeconds(60);
        while(true) { try {await using var c=await Admin.OpenConnectionAsync(); break;} catch(NpgsqlException) when(DateTime.UtcNow<until){await Task.Delay(50);} }
        await using var a=await Admin.OpenConnectionAsync();
        await Workload.Execute(a,"CREATE ROLE tt032_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD 'tt032-disposable'");
    }
    public string ForDatabase(string name,bool admin=false)
    {
        var b=new NpgsqlConnectionStringBuilder(ConnectionString){Database=name,MaxPoolSize=4};
        if(!admin){b.Username="tt032_app";b.Password="tt032-disposable";}
        return b.ConnectionString;
    }
    public async Task<object> Snapshot()
    {
        var containers=new List<object>();
        foreach(var id in Ids)
        {
            var i=await Docker.Containers.InspectContainerAsync(id);
            containers.Add(new{id,i.Image,i.Name,i.State.Status,i.HostConfig.Memory,i.HostConfig.NanoCPUs,i.HostConfig.CpusetCpus,
                Mounts=i.Mounts.Select(m=>new{m.Type,m.Name,m.Destination})});
        }
        if(Admin is null) return new{containers,ClientWorkingSet=Process.GetCurrentProcess().WorkingSet64};
        await using var c=await Admin.OpenConnectionAsync();
        var data=await Workload.Scalar<string>(c,"""
            SELECT json_build_object('version',version(),'fsync',current_setting('fsync'),'synchronous_commit',current_setting('synchronous_commit'),'shared_buffers',current_setting('shared_buffers'),'max_connections',current_setting('max_connections'),'databases',(SELECT json_agg(json_build_object('name',datname,'oid',oid,'bytes',pg_database_size(oid))) FROM pg_database WHERE NOT datistemplate),'sessions',(SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend'))::text
            """);
        var psi=new ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
        foreach(var arg in new[]{"--host","unix:///var/run/docker.sock","exec",PgId!,"du","-sk","/var/lib/postgresql/data"})psi.ArgumentList.Add(arg);
        using var du=Process.Start(psi)!;var stdout=du.StandardOutput.ReadToEndAsync();var stderr=du.StandardError.ReadToEndAsync();await du.WaitForExitAsync();
        Workload.Require(du.ExitCode==0,"DiskObservation:"+await stderr);
        var diskKiB=long.Parse((await stdout).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[0]);
        return new{containers,Postgres=JsonSerializer.Deserialize<JsonElement>(data),PostgresAllocatedKiB=diskKiB,ClientWorkingSet=Process.GetCurrentProcess().WorkingSet64};
    }
    public async ValueTask DisposeAsync()
    {
        if(Admin is not null){await Admin.DisposeAsync();Admin=null;}
        foreach(var id in Ids.AsEnumerable().Reverse())
        {
            try{ await Docker.Containers.RemoveContainerAsync(id,new(){Force=true,RemoveVolumes=true}); }
            catch(DockerContainerNotFoundException) { }
            var remaining=await Docker.Containers.ListContainersAsync(new(){All=true,Filters=new Dictionary<string,IDictionary<string,bool>>{{"id",new Dictionary<string,bool>{{id,true}}}}});
            Cleanup.Add(new{Container=id,Absent=remaining.Count==0});
            Workload.Require(remaining.Count==0,"ContainerCleanup");
        }
        var all=await Docker.Volumes.ListAsync();
        foreach(var v in Volumes){var absent=all.Volumes.All(x=>x.Name!=v);Cleanup.Add(new{Volume=v,Absent=absent});Workload.Require(absent,"VolumeCleanup");}
    }
    public async Task VerifyAbsent()
    {
        var all=await Docker.Containers.ListContainersAsync(new(){All=true});
        foreach(var id in Ids)Workload.Require(all.All(c=>c.ID!=id),"FinalContainerAbsence:"+id);
        var volumes=await Docker.Volumes.ListAsync();
        foreach(var v in Volumes)Workload.Require(volumes.Volumes.All(x=>x.Name!=v),"FinalVolumeAbsence:"+v);
        Workload.Require(Ids.Count>0 && Cleanup.Count>=Ids.Count+Volumes.Count,"MissingCleanupReceipts");
    }
    public static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();var p=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return p;}
}

sealed class ControllerLogs : IAsyncDisposable
{
    readonly Process process;
    readonly Task pump;
    readonly StreamWriter writer;
    public System.Collections.Concurrent.ConcurrentDictionary<int,int> ReadyGenerations {get;}=new();
    public List<string> Errors {get;}=[];
    public ControllerLogs(string id,string path)
    {
        writer=new StreamWriter(path,false,new UTF8Encoding(false)){AutoFlush=true};
        process=new Process{StartInfo=new ProcessStartInfo("docker"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false}};
        foreach(var a in new[]{"--host","unix:///var/run/docker.sock","logs","--follow",id})process.StartInfo.ArgumentList.Add(a);
        process.Start();
        pump=Task.WhenAll(Read(process.StandardOutput),Read(process.StandardError));
    }
    async Task Read(StreamReader reader)
    {
        while(await reader.ReadLineAsync() is {} line)
        {
            lock(writer){writer.WriteLine(line);}
            try
            {
                using var doc=JsonDocument.Parse(line); var r=doc.RootElement;
                if(r.TryGetProperty("message",out var eventMessage)&&eventMessage.GetString()=="ready"&&r.TryGetProperty("poolHash",out _))
                {
                    var id=r.GetProperty("id").GetInt32();var generation=r.GetProperty("generation").GetInt32();
                    ReadyGenerations.AddOrUpdate(id,generation,(_,old)=>Math.Max(old,generation));
                }
                if(r.TryGetProperty("level",out var level)&&level.GetString()=="error")lock(Errors)Errors.Add(line);
            }catch(JsonException){}
        }
    }
    public async ValueTask DisposeAsync()
    {
        if(!process.HasExited)process.Kill(entireProcessTree:true);
        await process.WaitForExitAsync();await pump;writer.Dispose();process.Dispose();
    }
}

sealed class ResourceMonitor : IAsyncDisposable
{
    readonly Host host;
    readonly CancellationTokenSource stop=new();
    readonly Task run;
    readonly long started=Clock.Now();
    public System.Collections.Concurrent.ConcurrentQueue<object> Samples {get;}=new();
    public List<string> Errors {get;}=[];
    sealed class Capture : IProgress<ContainerStatsResponse>
    {public ContainerStatsResponse? Value;public void Report(ContainerStatsResponse value)=>Value=value;}
    public ResourceMonitor(Host host){this.host=host;run=Observe();}
    async Task Observe()
    {
        try
        {
            while(!stop.IsCancellationRequested)
            {
                foreach(var id in host.Ids.ToArray())
                {
                    var c=new Capture();await Host.Docker.Containers.GetContainerStatsAsync(id,new(){Stream=false,OneShot=true},c,stop.Token);
                    if(c.Value is not {} stats)throw new InvalidDataException("MissingMemorySample");
                    Samples.Enqueue(new{ElapsedMs=Clock.Ms(started),Container=id,MemoryBytes=stats.MemoryStats.Usage,LimitBytes=stats.MemoryStats.Limit,ClientWorkingSet=Process.GetCurrentProcess().WorkingSet64});
                }
                await Task.Delay(500,stop.Token);
            }
        }catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception e){Errors.Add(e.ToString());}
    }
    public async ValueTask DisposeAsync(){stop.Cancel();await run;stop.Dispose();}
}

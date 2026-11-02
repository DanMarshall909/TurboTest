"""One-off TT-036 experiment. Serial fresh containers; four independent DB workers."""
import argparse, hashlib, json, os, pathlib, subprocess, time, uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
IMAGE = 'postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'
DOCKER = ['docker', '--host', 'unix:///var/run/docker.sock']
SETTINGS = {'fsync':'off', 'synchronous_commit':'off', 'full_page_writes':'off'}
CONFIGS = ['disk-logged','tmpfs-logged','disk-unlogged','tmpfs-unlogged']
PROFILE_SETTINGS = {'shared_preload_libraries':'pg_stat_statements','track_io_timing':'on',
                    'track_wal_io_timing':'on','compute_query_id':'on','pg_stat_statements.track':'top',
                    'pg_stat_statements.track_planning':'off','pg_stat_statements.track_utility':'on'}

def run(argv, **kw):
    return subprocess.run(argv, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120, **kw).stdout

def docker(*args): return run(DOCKER + list(args))
def hashes(paths): return {str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in paths}
def save(path, value): path.write_text(json.dumps(value, indent=2))
def inventory():
    return {'containers':docker('ps','-a','--no-trunc','--format','{{json .}}').decode(),
            'volumes':docker('volume','ls','--format','{{json .}}').decode()}
def host():
    s=os.statvfs('/var/lib/docker')
    return {'loadavg':pathlib.Path('/proc/loadavg').read_text().strip(), 'docker_free_bytes':s.f_bavail*s.f_frsize}
def memory(cid):
    return {n:docker('exec',cid,'cat','/sys/fs/cgroup/'+n).decode().strip() for n in
            ['memory.max','memory.swap.max','memory.current','memory.peak','memory.swap.current','memory.swap.peak','memory.events','memory.pressure','cpu.stat','io.stat']}

def main():
    p=argparse.ArgumentParser(); p.add_argument('--output',required=True); p.add_argument('--phase',choices=['smoke','profile','measure'],required=True); p.add_argument('--runtime',required=True); args=p.parse_args()
    out=ROOT/args.output; out.mkdir(parents=True,exist_ok=False)
    inputs=sorted(f for f in (ROOT/'experiments/tt036').iterdir() if f.is_file())
    inputs += [ROOT/'experiments/tt032/Workload.cs',ROOT/'Directory.Build.props',ROOT/'global.json']
    binary=ROOT/args.runtime/'StorageScreen.dll'
    executables=sorted(binary.parent.glob('*.dll'))+sorted(binary.parent.glob('*.json'))
    manifest={'head':run(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip(), 'sources':hashes(inputs), 'executables':hashes(executables),
              'image':IMAGE,'settings':SETTINGS,'phase':args.phase,'configs':CONFIGS,'runtime':args.runtime,'dotnet_info':run(['dotnet','--info'],cwd=ROOT).decode(),
              'uname':run(['uname','-a']).decode(),'logical_cpus':os.cpu_count()}
    save(out/'inputs.json',manifest); save(out/'inventory-before.json',inventory())
    failures=[]
    try:
        cases=[]
        for pair in range(5 if args.phase=='measure' else 1):
            order=CONFIGS[pair%4:]+CONFIGS[:pair%4]
            for config in order:
                for contender in (['respawn','cached'] if pair%2==0 else ['cached','respawn']):cases.append((pair,config,contender))
        for pair,config,contender in cases:
                name='tt036_'+uuid.uuid4().hex
                folder=out/f'{config}-{pair}-{contender}'; folder.mkdir()
                receipt={'name':name,'pair':pair,'config':config,'contender':contender,'cleanup':False}; cid=None; volumes=None
                save(folder/'receipt.json',receipt); started=time.monotonic()
                try:
                    assert manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables),'Input drift before run'
                    receipt['host_before']=host()
                    assert receipt['host_before']['docker_free_bytes']>=7*1024**3,'Disk headroom'
                    settings=SETTINGS|(PROFILE_SETTINGS if args.phase=='profile' else {})
                    flags=[v for k,value in settings.items() for v in ['-c',k+'='+value]]
                    mount=['--tmpfs','/var/lib/postgresql/data:rw,size=768m'] if config.startswith('tmpfs-') else []
                    cid=docker('create','--name',name,'--label','tt036.owner='+name,'--memory','1g','--memory-swap','1g',
                               *mount,'--network','bridge','-p','127.0.0.1::5432','-e','POSTGRES_PASSWORD=tt036-disposable',
                               '-e','POSTGRES_DB=screen',IMAGE,'postgres',*flags).decode().strip()
                    receipt['id']=cid
                    inspection=json.loads(docker('inspect',cid))[0]
                    save(folder/'container-created.json',inspection)
                    volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume'];receipt['volumes']=volumes
                    hc=inspection['HostConfig']
                    assert hc['Memory']==hc['MemorySwap']==1024**3 and hc['NetworkMode']=='bridge'
                    assert hc['NanoCpus']==0
                    expected_mount='tmpfs' if config.startswith('tmpfs-') else 'volume'
                    if expected_mount=='volume':
                        assert len(inspection['Mounts'])==1 and inspection['Mounts'][0]['Type']=='volume','Disk data mount'
                        assert inspection['Mounts'][0]['Destination']=='/var/lib/postgresql/data','Disk data destination'
                    else:
                        assert hc.get('Tmpfs',{}).get('/var/lib/postgresql/data')=='rw,size=768m','Tmpfs configuration'
                        assert not volumes,'Tmpfs must override the image data volume'
                    receipt['configuration']={'image_id':inspection['Image'],'memory':hc['Memory'],'memory_swap':hc['MemorySwap'],
                                              'network':hc['NetworkMode'],'nano_cpus':hc['NanoCpus'],'settings':settings,
                                              'mount_type':expected_mount,'tmpfs':hc.get('Tmpfs',{})}
                    docker('start',cid)
                    port=json.loads(docker('inspect',cid))[0]['NetworkSettings']['Ports']['5432/tcp'][0]['HostPort']
                    deadline=time.monotonic()+60
                    while True:
                        try:
                            docker('exec',cid,'psql','-U','postgres','-d','screen','-Atqc','SELECT 1');break
                        except subprocess.CalledProcessError:
                            if time.monotonic()>deadline:raise
                            time.sleep(.05)
                    receipt['container_preparation_seconds']=time.monotonic()-started
                    receipt['mountinfo']=docker('exec',cid,'cat','/proc/self/mountinfo').decode()
                    receipt['data_filesystem']=docker('exec',cid,'stat','-f','-c','%T','/var/lib/postgresql/data').decode().strip()
                    assert (receipt['data_filesystem']=='tmpfs')==config.startswith('tmpfs-')
                    receipt['memory_before']=memory(cid)
                    env=os.environ.copy();env['TT036_CONNECTION']=f'Host=127.0.0.1;Port={port};Database=screen;Username=postgres;Password=tt036-disposable;Maximum Pool Size=1;Timeout=5;Command Timeout=15;Enlist=false'
                    count=12 if args.phase=='smoke' else 100 if args.phase=='profile' else 1000
                    persistence=config.split('-')[1]
                    command=['dotnet',str(binary),contender,str(count),str(folder/'report.json'),persistence,args.phase]
                    receipt['command']=command
                    result=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,timeout=180)
                    (folder/'stdout.txt').write_bytes(result.stdout);(folder/'stderr.txt').write_bytes(result.stderr)
                    receipt['exit_code']=result.returncode
                    receipt['memory_after']=memory(cid)
                    report_path=folder/'report.json'
                    if report_path.exists():receipt['report_sha256']=hashlib.sha256(report_path.read_bytes()).hexdigest()
                    assert result.returncode==0,'BenchmarkFailed'
                    report=json.loads((folder/'report.json').read_text())
                    workers=1 if args.phase=='profile' else 4
                    assert report['Succeeded'] and report['Parallelism']==workers and report['Count']==count
                    assert report['Diagnostic']==(args.phase=='profile') and report['Unlogged']==(persistence=='unlogged')
                    assert len(report['Workers'])==workers and sum(len(w['Samples']) for w in report['Workers'])==count
                    expected=settings|{'session_replication_role':'origin','default_transaction_isolation':'read committed'}
                    if args.phase!='profile':expected|={'shared_preload_libraries':'','track_io_timing':'off','track_wal_io_timing':'off'}
                    assert all(report['Settings'][k]==v for k,v in expected.items()),'Settings mismatch'
                    assert receipt['memory_after']['memory.max']=='1073741824' and receipt['memory_after']['memory.swap.max']=='0'
                    receipt['inputs_unchanged']=manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables)
                    assert receipt['inputs_unchanged'],'Input drift after run'
                    print(f'{config} pair={pair} {contender} suite_ms={report["SuiteMs"]:.1f}',flush=True)
                except Exception as exc:
                    receipt['failure']=repr(exc);failures.append(str(folder))
                    if isinstance(exc,subprocess.CalledProcessError):
                        (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                    if isinstance(exc,subprocess.TimeoutExpired):
                        (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                    print(f'{config} pair={pair} {contender} failed: {exc}',flush=True)
                finally:
                    try:
                        if cid is None:
                            matches=docker('ps','-aq','--filter','name=^/'+name+'$').decode().split()
                            if matches:cid=matches[0]
                        if cid:
                            inspection=json.loads(docker('inspect',cid))[0]
                            assert inspection['Config']['Labels'].get('tt036.owner')==name,'Cleanup ownership'
                            volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume']
                            receipt['id']=cid;receipt['volumes']=volumes
                            try:
                                logs=subprocess.run(DOCKER+['logs',cid],capture_output=True,timeout=30)
                                (folder/'postgres.log').write_bytes(logs.stdout+logs.stderr)
                            except Exception as exc:receipt['log_error']=repr(exc)
                            docker('rm','-fv',cid)
                            assert not docker('ps','-aq','--filter','id='+cid).strip()
                            assert not set(volumes).intersection(docker('volume','ls','-q').decode().split())
                        receipt['cleanup']=True
                    finally:
                        receipt['total_seconds']=time.monotonic()-started;receipt['host_after']=host()
                        save(folder/'receipt.json',receipt)
                    assert receipt['cleanup'],'Cleanup unproven'
                if failures:raise RuntimeError('Retained failed attempt; repair before more runs: '+str(failures))
        save(out/'completed.json',{'completed':True,'failures':failures})
    finally:
        save(out/'inventory-after.json',inventory())

if __name__=='__main__':main()

"""One-off TT-035 experiment. Serial fresh containers; four independent DB workers."""
import argparse, hashlib, json, os, pathlib, subprocess, time, uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
IMAGE = 'postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'
DOCKER = ['docker', '--host', 'unix:///var/run/docker.sock']
SETTINGS = {'fsync':'off', 'synchronous_commit':'off', 'full_page_writes':'off'}

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
            ['memory.max','memory.swap.max','memory.current','memory.peak','memory.swap.current','memory.swap.peak','memory.events','memory.pressure']}

def main():
    p=argparse.ArgumentParser(); p.add_argument('--output',required=True); p.add_argument('--smoke',action='store_true'); args=p.parse_args()
    out=ROOT/args.output; out.mkdir(parents=True,exist_ok=False)
    inputs=sorted(f for f in (ROOT/'experiments/tt035').iterdir() if f.is_file())
    inputs += [ROOT/'experiments/tt032/Workload.cs',ROOT/'Directory.Build.props',ROOT/'global.json']
    binary=ROOT/'experiments/tt035/bin/Release/net10.0/TruncateScreen.dll'
    executables=sorted(binary.parent.glob('*.dll'))+sorted(binary.parent.glob('*.json'))
    manifest={'head':run(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip(), 'sources':hashes(inputs), 'executables':hashes(executables),
              'image':IMAGE,'settings':SETTINGS,'smoke':args.smoke,'dotnet_info':run(['dotnet','--info'],cwd=ROOT).decode(),
              'uname':run(['uname','-a']).decode(),'logical_cpus':os.cpu_count()}
    save(out/'inputs.json',manifest); save(out/'inventory-before.json',inventory())
    failures=[]
    try:
        for pair in range(1 if args.smoke else 5):
            for contender in (['respawn','cached'] if pair%2==0 else ['cached','respawn']):
                name='tt035_'+uuid.uuid4().hex
                folder=out/f'{pair}-{contender}'; folder.mkdir()
                receipt={'name':name,'pair':pair,'contender':contender,'cleanup':False}; cid=None; volumes=None
                save(folder/'receipt.json',receipt); started=time.monotonic()
                try:
                    assert manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables),'Input drift before run'
                    receipt['host_before']=host()
                    assert receipt['host_before']['docker_free_bytes']>=7*1024**3,'Disk headroom'
                    flags=[v for k,value in SETTINGS.items() for v in ['-c',k+'='+value]]
                    cid=docker('create','--name',name,'--label','tt035.owner='+name,'--memory','1g','--memory-swap','1g',
                               '--network','bridge','-p','127.0.0.1::5432','-e','POSTGRES_PASSWORD=tt035-disposable',
                               '-e','POSTGRES_DB=screen',IMAGE,'postgres',*flags).decode().strip()
                    receipt['id']=cid
                    inspection=json.loads(docker('inspect',cid))[0]
                    volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume'];receipt['volumes']=volumes
                    hc=inspection['HostConfig']
                    assert hc['Memory']==hc['MemorySwap']==1024**3 and hc['NetworkMode']=='bridge'
                    receipt['configuration']={'image_id':inspection['Image'],'memory':hc['Memory'],'memory_swap':hc['MemorySwap'],
                                              'network':hc['NetworkMode'],'nano_cpus':hc['NanoCpus'],'settings':SETTINGS}
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
                    receipt['memory_before']=memory(cid)
                    env=os.environ.copy();env['TT035_CONNECTION']=f'Host=127.0.0.1;Port={port};Database=screen;Username=postgres;Password=tt035-disposable;Maximum Pool Size=1;Timeout=5;Command Timeout=15;Enlist=false'
                    command=['dotnet',str(binary),contender,str(12 if args.smoke else 1000),str(folder/'report.json')]
                    receipt['command']=command
                    result=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,timeout=180)
                    (folder/'stdout.txt').write_bytes(result.stdout);(folder/'stderr.txt').write_bytes(result.stderr)
                    receipt['exit_code']=result.returncode
                    receipt['memory_after']=memory(cid)
                    report_path=folder/'report.json'
                    if report_path.exists():receipt['report_sha256']=hashlib.sha256(report_path.read_bytes()).hexdigest()
                    assert result.returncode==0,'BenchmarkFailed'
                    report=json.loads((folder/'report.json').read_text())
                    assert report['Succeeded'] and report['Parallelism']==4 and report['Count']==(12 if args.smoke else 1000)
                    assert len(report['Workers'])==4 and sum(len(w['Samples']) for w in report['Workers'])==report['Count']
                    expected=SETTINGS|{'session_replication_role':'origin','default_transaction_isolation':'read committed'}
                    assert all(report['Settings'][k]==v for k,v in expected.items()),'Settings mismatch'
                    assert receipt['memory_after']['memory.max']=='1073741824' and receipt['memory_after']['memory.swap.max']=='0'
                    receipt['inputs_unchanged']=manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables)
                    assert receipt['inputs_unchanged'],'Input drift after run'
                    print(f'pair={pair} {contender} suite_ms={report["SuiteMs"]:.1f}',flush=True)
                except Exception as exc:
                    receipt['failure']=repr(exc);failures.append(str(folder))
                    if isinstance(exc,subprocess.CalledProcessError):
                        (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                    if isinstance(exc,subprocess.TimeoutExpired):
                        (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                    print(f'pair={pair} {contender} failed: {exc}',flush=True)
                finally:
                    try:
                        if cid is None:
                            matches=docker('ps','-aq','--filter','name=^/'+name+'$').decode().split()
                            if matches:cid=matches[0]
                        if cid:
                            inspection=json.loads(docker('inspect',cid))[0]
                            assert inspection['Config']['Labels'].get('tt035.owner')==name,'Cleanup ownership'
                            volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume']
                            receipt['id']=cid;receipt['volumes']=volumes
                            logs=subprocess.run(DOCKER+['logs',cid],capture_output=True,timeout=30)
                            (folder/'postgres.log').write_bytes(logs.stdout+logs.stderr)
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

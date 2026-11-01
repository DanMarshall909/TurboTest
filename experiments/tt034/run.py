"""One-off TT-034 driver. Fresh owned containers; retain every attempt and cleanup receipt."""
import argparse, hashlib, io, json, os, pathlib, subprocess, tarfile, time, uuid, zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
IMAGE = 'postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'
ZIP_SHA = '2c0fbf993b1612337054c7c3a824f7abab02f126d9eff4e6aefd17fd56c81b0c'
DOCKER = ['docker', '--host', 'unix:///var/run/docker.sock']
BASE = {'fsync':'off','synchronous_commit':'off','full_page_writes':'off'}
PROFILES = {'relaxed':{}, 'minimal-wal':{'wal_level':'minimal','max_wal_senders':'0'},
            'no-jit':{'jit':'off'}, 'no-autovacuum':{'autovacuum':'off'}}

def run(argv, **kw):
    return subprocess.run(argv, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120, **kw).stdout

def docker(*args, **kw):
    return run(DOCKER + list(args), **kw)

def hashes(paths):
    return {str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in paths}

def host_observation():
    st=os.statvfs('/var/lib/docker')
    return {'loadavg':pathlib.Path('/proc/loadavg').read_text().strip(),'docker_volume_free_bytes':st.f_bavail*st.f_frsize}

def main():
    p = argparse.ArgumentParser(); p.add_argument('--output', required=True); p.add_argument('--smoke', action='store_true'); args=p.parse_args()
    out = ROOT / args.output; out.mkdir(parents=True, exist_ok=False)
    release = (ROOT/'artifacts/tt034/e-maj-5.0.1.zip').read_bytes()
    assert hashlib.sha256(release).hexdigest() == ZIP_SHA
    package=io.BytesIO()
    with zipfile.ZipFile(io.BytesIO(release)) as z, tarfile.open(fileobj=package,mode='w') as tar:
        for source, target in [('emaj.control','extension/emaj.control'),('sql/emaj--5.0.1.sql','emaj/emaj--5.0.1.sql')]:
            data=z.read('e-maj-5.0.1/'+source); info=tarfile.TarInfo(target); info.mode=0o644; info.size=len(data); tar.addfile(info,io.BytesIO(data))
    inputs = list((ROOT/'experiments/tt034').glob('*.cs')) + list((ROOT/'experiments/tt034').glob('*.csproj')) + list((ROOT/'experiments/tt034').glob('*.py')) + list((ROOT/'experiments/tt034').glob('*.md'))
    inputs += [ROOT/'experiments/tt034/packages.lock.json',ROOT/'experiments/tt032/Workload.cs',ROOT/'Directory.Build.props',ROOT/'global.json']
    binary=ROOT/'experiments/tt034/bin/Release/net10.0/EmajScreen.dll'
    executables=sorted(binary.parent.glob('*.dll'))+sorted(binary.parent.glob('*.json'))
    manifest={'head':run(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip(), 'image':IMAGE,'zip_sha256':ZIP_SHA,
              'sources':hashes(inputs), 'executables':hashes(executables), 'profiles':PROFILES,'smoke':args.smoke,
              'dotnet_info':run(['dotnet','--info'],cwd=ROOT).decode(), 'uname':run(['uname','-a']).decode(), 'logical_cpus':os.cpu_count()}
    (out/'inputs.json').write_text(json.dumps(manifest,indent=2))
    failures=[]
    for pair in range(1 if args.smoke else 5):
        order=list(PROFILES); order=order[pair%4:]+order[:pair%4]
        for profile in (['relaxed'] if args.smoke else order):
            for contender in (['respawn','emaj'] if pair%2==0 else ['emaj','respawn']):
                name='tt034_'+uuid.uuid4().hex
                folder=out/f'{profile}-{pair}-{contender}';folder.mkdir()
                receipt={'name':name,'profile':profile,'pair':pair,'contender':contender,'cleanup':False}; cid=None; volumes=None
                (folder/'receipt.json').write_text(json.dumps(receipt,indent=2))
                started=time.monotonic()
                try:
                    assert manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables),'Input drift before run'
                    receipt['host_before']=host_observation()
                    assert receipt['host_before']['docker_volume_free_bytes']>=7*1024**3,'Disk headroom below admitted growth plus reserve'
                    flags=[]
                    for k,v in (BASE | PROFILES[profile]).items(): flags+=['-c',k+'='+v]
                    cid=docker('create','--name',name,'--label','tt034.owner='+name,'--memory','256m','-p','127.0.0.1::5432',
                               '-e','POSTGRES_PASSWORD=tt034-disposable','-e','POSTGRES_DB=screen',IMAGE,'postgres',*flags).decode().strip()
                    receipt['id']=cid
                    inspection=json.loads(docker('inspect',cid))[0]
                    volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume'];receipt['volumes']=volumes
                    docker('start',cid)
                    port=json.loads(docker('inspect',cid))[0]['NetworkSettings']['Ports']['5432/tcp'][0]['HostPort']
                    deadline=time.monotonic()+60
                    while True:
                        try:
                            docker('exec',cid,'psql','-U','postgres','-d','screen','-Atqc','SELECT 1');break
                        except subprocess.CalledProcessError:
                            if time.monotonic()>deadline:raise
                            time.sleep(.05)
                    # Install release files into the owned container only; CREATE EXTENSION
                    # preserves E-Maj's supported superuser rollback optimization.
                    sharedir=docker('exec',cid,'pg_config','--sharedir').decode().strip()
                    docker('cp','-',cid+':'+sharedir,input=package.getvalue())
                    receipt['container_preparation_seconds']=time.monotonic()-started
                    receipt['configuration']={'image_id':inspection['Image'],'memory':inspection['HostConfig']['Memory'],'nano_cpus':inspection['HostConfig']['NanoCpus'],'flags':BASE|PROFILES[profile]}
                    env=os.environ.copy();env['TT034_CONNECTION']=f'Host=127.0.0.1;Port={port};Database=screen;Username=postgres;Password=tt034-disposable;Maximum Pool Size=1;Timeout=5;Command Timeout=15;Enlist=false'
                    result=subprocess.run(['dotnet',str(binary),contender,str(3 if args.smoke else 100),str(folder/'report.json')],cwd=ROOT,env=env,capture_output=True,timeout=120)
                    (folder/'stdout.txt').write_bytes(result.stdout);(folder/'stderr.txt').write_bytes(result.stderr)
                    receipt['exit_code']=result.returncode
                    report_path=folder/'report.json'
                    receipt['report_sha256']=hashlib.sha256(report_path.read_bytes()).hexdigest()
                    report=json.loads(report_path.read_text())
                    assert report['Contender']==contender and report['Count']==(3 if args.smoke else 100),'Run identity'
                    if result.returncode==0:
                        assert report['Succeeded'] and len(report['Samples'])==report['Count'],'Incomplete run'
                        assert [s['Ordinal'] for s in report['Samples']]==list(range(report['Count'])),'Incomplete ordinals'
                        assert report['Settings']['server_version']=='16.15','Server version'
                        expected={'fsync':'off','synchronous_commit':'off','full_page_writes':'off','wal_level':'replica',
                                  'max_wal_senders':'10','jit':'on','autovacuum':'on','archive_mode':'off',
                                  'session_replication_role':'origin','default_transaction_isolation':'read committed'} | PROFILES[profile]
                        assert all(report['Settings'][k]==v for k,v in expected.items()),'Observed settings mismatch'
                    receipt['inputs_unchanged']=manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables)
                    assert receipt['inputs_unchanged'],'Input drift after run'
                    receipt['stats']=docker('stats','--no-stream','--format','{{json .}}',cid).decode().strip()
                    if result.returncode: failures.append(str(folder));receipt['failure']='BenchmarkFailed'
                    print(f'{profile} pair={pair} {contender} exit={result.returncode}',flush=True)
                except Exception as exc:
                    receipt['failure']=repr(exc);failures.append(str(folder))
                    if isinstance(exc,subprocess.CalledProcessError):
                        (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                    print(f'{profile} pair={pair} {contender} failed: {exc}',flush=True)
                finally:
                    # Do not lose an uncertain create result: recover only by unique owned name.
                    try:
                        if cid is None:
                            matches=docker('ps','-aq','--filter','name=^/'+name+'$').decode().split()
                            if matches: cid=matches[0]
                        if cid and volumes is None:
                            inspection=json.loads(docker('inspect',cid))[0]
                            assert inspection['Config']['Labels'].get('tt034.owner')==name
                            volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume']
                            receipt['id']=cid;receipt['volumes']=volumes
                        if cid:
                            try:
                                logs=subprocess.run(DOCKER+['logs',cid],capture_output=True,timeout=30)
                                (folder/'postgres.log').write_bytes(logs.stdout+logs.stderr)
                            except Exception as log_error: receipt['log_error']=repr(log_error)
                            docker('rm','-fv',cid)
                            assert not docker('ps','-aq','--filter','id='+cid).strip()
                            live=set(docker('volume','ls','-q').decode().split())
                            assert not live.intersection(volumes)
                        receipt['cleanup']=True
                    finally:
                        receipt['total_seconds']=time.monotonic()-started
                        receipt['host_after']=host_observation()
                        (folder/'receipt.json').write_text(json.dumps(receipt,indent=2))
                    assert receipt['cleanup'], 'Cleanup must be proven before another run'
                if failures: raise RuntimeError('Retained failed attempt; repair before further runs: '+str(failures))
    assert manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables),'Input drift'
    (out/'completed.json').write_text(json.dumps({'failures':failures,'completed':True}))

if __name__ == '__main__': main()

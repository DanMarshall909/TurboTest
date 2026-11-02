"""One-off ready-pool experiment; lifecycle helpers reused from the owned TT-036 driver."""
import argparse,importlib.util,json,os,pathlib,subprocess,time,uuid

HERE=pathlib.Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('storage_driver',HERE.parent/'run.py')
shared=importlib.util.module_from_spec(spec);spec.loader.exec_module(shared)
ROOT=shared.ROOT;docker=shared.docker;save=shared.save;hashes=shared.hashes
MODES=['direct','awaited','background'];DELAYS=[0,5,20]

def main():
    p=argparse.ArgumentParser();p.add_argument('--phase',choices=['smoke','measure'],required=True);p.add_argument('--output',required=True);p.add_argument('--runtime',required=True);a=p.parse_args()
    out=ROOT/a.output;out.mkdir(parents=True,exist_ok=False)
    inputs=sorted(f for f in HERE.iterdir() if f.is_file())+[HERE.parent/'run.py',ROOT/'experiments/tt032/Workload.cs',ROOT/'Directory.Build.props',ROOT/'global.json']
    binary=ROOT/a.runtime/'PipelineScreen.dll';executables=sorted(binary.parent.glob('*.dll'))+sorted(binary.parent.glob('*.json'))
    manifest={'head':shared.run(['git','rev-parse','HEAD'],cwd=ROOT).decode().strip(),'phase':a.phase,'sources':hashes(inputs),
              'executables':hashes(executables),'image':shared.IMAGE,'settings':shared.SETTINGS,'modes':MODES,'delays_ms':DELAYS,
              'dotnet_info':shared.run(['dotnet','--info'],cwd=ROOT).decode(),'uname':shared.run(['uname','-a']).decode(),'logical_cpus':os.cpu_count()}
    save(out/'inputs.json',manifest);save(out/'inventory-before.json',shared.inventory())
    cases=[]
    for pair in range(1 if a.phase=='smoke' else 5):
        delays=DELAYS[pair%3:]+DELAYS[:pair%3]
        for index,delay in enumerate(delays):
            modes=MODES if pair%2==0 else list(reversed(MODES));cut=(pair+index)%3;modes=modes[cut:]+modes[:cut]
            cases += [(pair,delay,mode,False) for mode in modes]
    if a.phase=='smoke':cases.append((0,0,'background',True))
    failures=[]
    try:
        for pair,delay,mode,fault in cases:
            folder=out/f'delay{delay}-{pair}-{mode}{"-fault" if fault else ""}';folder.mkdir()
            name='tt036_pipeline_'+uuid.uuid4().hex;cid=None;volumes=[];started=time.monotonic()
            receipt={'name':name,'pair':pair,'delay_ms':delay,'mode':mode,'fault':fault,'cleanup':False};save(folder/'receipt.json',receipt)
            try:
                assert manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables),'Input drift before case'
                receipt['host_before']=shared.host();assert receipt['host_before']['docker_free_bytes']>=7*1024**3
                flags=[v for k,value in shared.SETTINGS.items() for v in ['-c',k+'='+value]]
                cid=docker('create','--name',name,'--label','tt036.pipeline.owner='+name,'--memory','1g','--memory-swap','1g',
                           '--network','bridge','-p','127.0.0.1::5432','-e','POSTGRES_PASSWORD=tt036-disposable',
                           '-e','POSTGRES_DB=screen',shared.IMAGE,'postgres',*flags).decode().strip();receipt['id']=cid
                inspection=json.loads(docker('inspect',cid))[0];save(folder/'container-created.json',inspection)
                volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume'];receipt['volumes']=volumes
                hc=inspection['HostConfig'];assert hc['Memory']==hc['MemorySwap']==1024**3 and hc['NetworkMode']=='bridge' and hc['NanoCpus']==0 and len(volumes)==1
                receipt['configuration']={'image_id':inspection['Image'],'memory':hc['Memory'],'memory_swap':hc['MemorySwap'],'network':hc['NetworkMode'],'nano_cpus':hc['NanoCpus'],'settings':shared.SETTINGS}
                docker('start',cid);port=json.loads(docker('inspect',cid))[0]['NetworkSettings']['Ports']['5432/tcp'][0]['HostPort']
                deadline=time.monotonic()+60
                while True:
                    try:docker('exec',cid,'psql','-U','postgres','-d','screen','-Atqc','SELECT 1');break
                    except subprocess.CalledProcessError:
                        if time.monotonic()>deadline:raise
                        time.sleep(.05)
                receipt['container_preparation_seconds']=time.monotonic()-started;receipt['memory_before']=shared.memory(cid)
                env=os.environ.copy();env['TT036_CONNECTION']=f'Host=127.0.0.1;Port={port};Database=screen;Username=postgres;Password=tt036-disposable;Maximum Pool Size=1;Timeout=5;Command Timeout=15;Enlist=false'
                count=12 if a.phase=='smoke' else 1000
                command=['dotnet',str(binary),mode,str(count),str(delay),str(folder/'report.json')]+(['fault'] if fault else [])
                receipt['command']=command;r=subprocess.run(command,cwd=ROOT,env=env,capture_output=True,timeout=180)
                (folder/'stdout.txt').write_bytes(r.stdout);(folder/'stderr.txt').write_bytes(r.stderr);receipt['exit_code']=r.returncode
                receipt['memory_after']=shared.memory(cid)
                report=json.loads((folder/'report.json').read_text());receipt['report_sha256']=shared.hashlib.sha256((folder/'report.json').read_bytes()).hexdigest()
                if fault:
                    assert r.returncode==1 and not report['Succeeded'] and 'TT036InjectedFailureAfterCommittedTruncate' in report['Failure']
                    state=report['PoolSettlement'];assert state['Stopped'] and state['Queued']==state['ActiveResets']==state['FailedSlotReadyPublications']==0
                    assert state['FaultedSlotId'] is not None and state['FailedSlots']>0 and report['FaultedSlotParentRows']==0
                    assert next(s for s in report['Slots'] if s['Id']==state['FaultedSlotId'])['State']==3
                    receipt['expected_fault_passed']=True
                else:
                    assert r.returncode==0 and report['Succeeded'] and report['Count']==count and len(report['Batch']['Samples'])==count
                    assert report['Batch']['ReadySlots']==report['SlotCount']==(4 if mode=='direct' else 8)
                    assert all(s['State']==0 and s['Qualification'] for s in report['Slots'])
                    expected=shared.SETTINGS|{'session_replication_role':'origin','default_transaction_isolation':'read committed','shared_preload_libraries':'','track_io_timing':'off','track_wal_io_timing':'off'}
                    assert all(report['Settings'][k]==v for k,v in expected.items())
                    if mode!='direct':assert report['PoolSettlement']['Stopped'] and report['PoolSettlement']['Queued']==report['PoolSettlement']['ActiveResets']==report['PoolSettlement']['FailedSlots']==0
                assert receipt['memory_after']['memory.max']=='1073741824' and receipt['memory_after']['memory.swap.max']=='0'
                receipt['inputs_unchanged']=manifest['sources']==hashes(inputs) and manifest['executables']==hashes(executables);assert receipt['inputs_unchanged']
                print(f'delay={delay} pair={pair} {mode} '+('fault surfaced/settled' if fault else f'suite_ms={report["Batch"]["SuiteMs"]:.1f}'),flush=True)
            except Exception as exc:
                receipt['failure']=repr(exc);failures.append(str(folder))
                if isinstance(exc,(subprocess.CalledProcessError,subprocess.TimeoutExpired)):
                    (folder/'failure.stdout').write_bytes(exc.stdout or b'');(folder/'failure.stderr').write_bytes(exc.stderr or b'')
                print(f'delay={delay} pair={pair} {mode} failed: {exc}',flush=True)
            finally:
                try:
                    if cid is None:
                        matches=docker('ps','-aq','--filter','name=^/'+name+'$').decode().split()
                        if matches:cid=matches[0]
                    if cid:
                        inspection=json.loads(docker('inspect',cid))[0];assert inspection['Config']['Labels'].get('tt036.pipeline.owner')==name
                        volumes=[m['Name'] for m in inspection['Mounts'] if m['Type']=='volume'];receipt['id']=cid;receipt['volumes']=volumes
                        try:
                            logs=subprocess.run(shared.DOCKER+['logs',cid],capture_output=True,timeout=30);(folder/'postgres.log').write_bytes(logs.stdout+logs.stderr)
                        except Exception as exc:receipt['log_error']=repr(exc)
                        docker('rm','-fv',cid);assert not docker('ps','-aq','--filter','id='+cid).strip()
                        assert not set(volumes).intersection(docker('volume','ls','-q').decode().split())
                    receipt['cleanup']=True
                finally:
                    receipt['total_seconds']=time.monotonic()-started;receipt['host_after']=shared.host();save(folder/'receipt.json',receipt)
                assert receipt['cleanup']
            if failures:raise RuntimeError('Retained failed attempt; repair before more cases: '+str(failures))
        save(out/'completed.json',{'completed':True,'failures':[]})
    finally:save(out/'inventory-after.json',shared.inventory())

if __name__=='__main__':main()

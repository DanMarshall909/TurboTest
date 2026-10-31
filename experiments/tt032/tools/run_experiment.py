#!/usr/bin/env python3
"""TT-032 one-off experiment supervisor. Retains every attempt; never retries cases."""
import argparse, hashlib, itertools, json, os, pathlib, random, signal, subprocess, sys, time
ROOT = pathlib.Path(__file__).resolve().parents[3]
DLL = ROOT / 'experiments/tt032/bin/Release/net10.0/CompetitiveBaseline.dll'
NAMES = ['native-template', 'integresql', 'respawn', 'turbotest']

def command(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True).strip()

def save(path, value):
    path.write_text(json.dumps(value, indent=2)+'\n')

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def inputs():
    paths = [p for p in command('git','ls-files').splitlines()
             if p.startswith(('src/', 'experiments/tt032/')) or p in ('Directory.Build.props','global.json','.ai-rules.json')]
    binaries = sorted(DLL.parent.glob('*.dll')) + sorted(DLL.parent.glob('*.json'))
    return {'head': command('git','rev-parse','HEAD'), 'tree':command('git','rev-parse','HEAD^{tree}'),
            'status':command('git','status','--porcelain'),
            'files':{p:digest(ROOT/p) for p in paths},
            'binaries':{str(p.relative_to(ROOT)):digest(p) for p in binaries},
            'sdk':command('dotnet','--version')}

def require_identity(bound, stage):
    if inputs()!=bound: raise RuntimeError('ExperimentInputDrift:'+stage)

def inventory():
    return sorted(command('docker','--host','unix:///var/run/docker.sock','ps','-a','--format','{{.ID}} {{.Names}}').splitlines())

def make_plan(phase, rounds, selections):
    rng = random.Random(20261031)
    scenarios = [('durable',1,1),('durable',20,1),('durable',100,4),
                 ('relaxed',1,1),('relaxed',20,1),('relaxed',1000,4)]
    if phase=='tune': scenarios=[('durable',20,4),('relaxed',100,4)]
    plan=[]
    for round_id in range(rounds):
        groups=list(itertools.product(scenarios, ['warm'] if phase=='tune' else ['fresh','warm']))
        rng.shuffle(groups)
        for scenario,mode in groups:
            configuration,count,workers=scenario
            order=NAMES[round_id%4:]+NAMES[:round_id%4]
            if round_id%2: order=list(reversed(order))
            for name in order:
                settings=[1,4] if phase=='tune' else [selections[configuration][name] if workers==4 else 1]
                if round_id%2: settings.reverse()
                for reset in settings:
                    plan.append(dict(round=round_id,contender=name,configuration=configuration,count=count,
                                     workers=workers,capacity=workers,mode=mode,reset_workers=reset,lifetime_ms=25))
    return plan

def validate_plan(plan,phase,rounds):
    expected_per_round=16 if phase=='tune' else 48
    assert len(plan)==rounds*expected_per_round, 'PlanCount'
    keys=[tuple(x.items()) for x in plan]
    assert len(keys)==len(set(keys)), 'DuplicateCase'
    for r in range(rounds):
        rows=[x for x in plan if x['round']==r]
        assert len(rows)==expected_per_round, 'RoundCount'
        assert {x['contender'] for x in rows}==set(NAMES), 'ContenderMembership'
        for name in NAMES: assert sum(x['contender']==name for x in rows)==expected_per_round//4, 'ContenderBalance'


def main():
    p=argparse.ArgumentParser();p.add_argument('phase',choices=['tune','representative']);p.add_argument('--rounds',type=int,required=True)
    p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--selections',type=pathlib.Path)
    args=p.parse_args();out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
    selections=json.loads(args.selections.read_text()) if args.selections else None
    if args.phase=='representative' and selections is None: raise ValueError('RepresentativeRequiresFrozenSelections')
    plan=make_plan(args.phase,args.rounds,selections);validate_plan(plan,args.phase,args.rounds);save(out/'plan.json',plan)
    bound=inputs()
    if bound['status']: raise ValueError('ExperimentRequiresCleanCheckpoint:'+bound['status'])
    manifest={'inputs':bound,'plan_sha256':digest(out/'plan.json'),'docker':json.loads(command('docker','--host','unix:///var/run/docker.sock','version','--format','{{json .Server}}')),
              'platform':command('uname','-a'),'cpu':json.loads(command('lscpu','--json')),'filesystem':json.loads(command('findmnt','-T',str(ROOT),'-J')),
              'images':[], 'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()), 'selections':selections}
    for ref in ['postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea','ghcr.io/allaboutapps/integresql@sha256:66b7433399f1907bad9e4b7cc7bd528ed4ebfccddecb8fd65da4af4d06a68c5a']:
        i=json.loads(command('docker','image','inspect',ref))[0]
        manifest['images'].append({k:i.get(k) for k in ['Id','RepoDigests','Size','Architecture','Os']})
    save(out/'manifest.json',manifest);baseline_inventory=inventory();save(out/'initial-containers.json',baseline_inventory)
    receipts=[];common_binding=None
    for index,case in enumerate(plan):
        require_identity(bound, 'BeforeCase')
        case_dir=out/f'{index:03d}-{case["contender"]}-{case["configuration"]}-{case["mode"]}-{case["count"]}-r{case["round"]}-w{case["reset_workers"]}'
        argv=['dotnet',str(DLL),'run',case['contender'],str(case['count']),str(case['workers']),case['mode'],str(case_dir),str(case['lifetime_ms']),case['configuration']]
        env=dict(os.environ,TT032_RESET_WORKERS=str(case['reset_workers']),TT032_CAPACITY=str(case['capacity']),TT032_TRACE='0')
        receipt={'case':case,'command':argv,'cwd':str(ROOT),'environment':{k:env[k] for k in ['TT032_RESET_WORKERS','TT032_CAPACITY']},
                 'before_loadavg':pathlib.Path('/proc/loadavg').read_text().strip(),'timeout_seconds':1500}
        started=time.perf_counter()
        with open(out/f'{index:03d}.stdout.txt','w') as log:
            child=subprocess.Popen(argv,cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            receipt['pid']=child.pid
            try: code=child.wait(timeout=1500)
            except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
                receipt['interruption']=type(error).__name__;os.killpg(child.pid,signal.SIGINT)
                try: code=child.wait(timeout=60)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid,signal.SIGKILL);code=child.wait();receipt['forced_kill']=True
        receipt.update(exit_code=code,process_wall_ms=(time.perf_counter()-started)*1000,
                       after_loadavg=pathlib.Path('/proc/loadavg').read_text().strip(),post_containers=inventory())
        receipt['inputs_unchanged']=inputs()==bound
        report_path=case_dir/'report.json'
        if report_path.exists():
            report=json.loads(report_path.read_text());receipt['report_succeeded']=report['Succeeded'];receipt['report_sha256']=digest(report_path)
            binding=report['Binding']
            if common_binding is None:common_binding=binding
            receipt['binding_matches_experiment']=binding==common_binding and binding['AssemblySha'].lower()==bound['binaries'][str(DLL.relative_to(ROOT))] and binding['TurboSha'].lower()==bound['binaries'][str((DLL.parent/'TurboTest.EFCore.dll').relative_to(ROOT))]
            image_ids={i['Id'] for i in manifest['images']}
            receipt['image_binding_matches']=all(c['Image'] in image_ids for boundary in ['InitialResources','FinalResources'] if boundary in report for c in report[boundary]['containers'])
        else: receipt['report_succeeded']=False;receipt['missing_report']=True
        receipts.append(receipt);save(out/'receipts.json',receipts)
        print(json.dumps({'finished':index+1,'planned':len(plan),**case,'exit':code,'wall_ms':receipt['process_wall_ms'],'succeeded':receipt['report_succeeded']}),flush=True)
        if not receipt['inputs_unchanged'] or not receipt.get('binding_matches_experiment',False) or not receipt.get('image_binding_matches',False):raise RuntimeError('ExperimentIdentityRejected')
        if receipt['post_containers']!=baseline_inventory:raise RuntimeError('UnsettledResourceInventory')
        if 'interruption' in receipt:raise RuntimeError('ExperimentInterrupted; no cases retried')
    save(out/'completion.json',{'completed':len(receipts),'planned':len(plan),'all_passed':all(r['exit_code']==0 and r['report_succeeded'] for r in receipts),'ended_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())})
    return 0 if all(r['exit_code']==0 and r['report_succeeded'] for r in receipts) else 1

if __name__=='__main__':sys.exit(main())

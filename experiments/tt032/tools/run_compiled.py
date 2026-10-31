#!/usr/bin/env python3
"""TT-033 bounded qualification and paired measurement; no retry/resume API."""
import argparse, json, os, pathlib, signal, subprocess, time
import run_experiment as common

ROOT, DLL = common.ROOT, common.DLL
PG = 'postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'

def plan():
    return [{'round':r,'contender':name,'count':1000,'workers':4,'capacity':4,
             'reset_workers':4,'mode':'warm','configuration':'relaxed'}
            for r in range(5) for name in (['respawn','compiled'] if r%2==0 else ['compiled','respawn'])]

def validate_plan(value):
    if value!=plan():raise ValueError('PairedPlanMismatch')

def validate_run(report,case,image_ids):
    fields={'Contender':'contender','Configuration':'configuration','Count':'count','Workers':'workers','Capacity':'capacity','ResetWorkers':'reset_workers','Mode':'mode'}
    if any(report[k]!=case[v] for k,v in fields.items()):raise ValueError('RunScenarioMismatch')
    if not common.image_binding(report,image_ids):raise ValueError('ImageBindingRejected')
    for key in ['InitialResources','FinalResources']:
        if key in report and any(report[key]['Postgres'][setting]!='off' for setting in ['fsync','synchronous_commit','full_page_writes']):raise ValueError('DurabilityMismatch')
    if report['Succeeded'] and (len(report['Samples'])!=case['count'] or report['SettledState']['Ready']!=case['capacity'] or report['SettledState']['ResetFailures']!=0):raise ValueError('CompleteWorkflowRejected')

def binding_matches(report, bound):
    b=report['Binding']
    expected={p:h for p,h in bound['files'].items() if p.startswith('experiments/tt032/') and len(pathlib.PurePosixPath(p).parts)==3}
    return (b['AssemblySha'].lower()==bound['binaries'][str(DLL.relative_to(ROOT))]
            and b['TurboSha'].lower()==bound['binaries'][str((DLL.parent/'TurboTest.EFCore.dll').relative_to(ROOT))]
            and {k:v.lower() for k,v in b['Inputs'].items()}==expected)

def qualification_report(path, bound, image_ids, expected_name):
    report=json.loads(path.read_text())
    if not report['Succeeded'] or report['Failures']:raise ValueError('QualificationFailed')
    if report['Contender']!=expected_name:raise ValueError('QualificationContender')
    if (report['Configuration'],report['Capacity'],report['ResetWorkers'])!=('relaxed',4,4):raise ValueError('QualificationConfiguration')
    if not binding_matches(report,bound):raise ValueError('QualificationBinding')
    if not report['Cleanup'] or not all(c['Absent'] for c in report['Cleanup']):raise ValueError('QualificationCleanup')
    if not report['InitialResources']['containers'] or not all(c['Image'] in image_ids for c in report['InitialResources']['containers']):raise ValueError('QualificationImage')
    if any(report['InitialResources']['Postgres'][setting]!='off' for setting in ['fsync','synchronous_commit','full_page_writes']):raise ValueError('QualificationDurability')
    ids={p['Id'] for p in report['Probes'] if p.get('Passed') is True}
    required={'fresh-migration-and-seed','idempotent-migration','concurrent-isolation-repeated-and-reordered',
              'oracle-rejects-seed','oracle-rejects-sequence','oracle-rejects-workload','failed-test-restores-committed-writes',
              'cancelled-admission-does-not-consume-capacity','cancelled-sql-settles-and-restores',
              'schema-replacement-and-stale-context','bounded-workload-connections-are-reused',
              'reset-failure-characterized-and-owned-resources-settle','independent-final-resource-absence',
              'fresh-fixture-recovers-after-server-failure'}
    if report['Contender']=='compiled':required.add('post-sequence-failure-quarantines-and-replaces')
    if not required<=ids or any(p.get('Passed') is False for p in report['Probes']):raise ValueError('QualificationProbes')
    recovery=path.parent/'fresh-recovery/report.json';data=json.loads(recovery.read_text())
    if not data['Succeeded'] or not binding_matches(data,bound) or not common.image_binding(data,image_ids):raise ValueError('QualificationFreshRecovery')
    validate_run(data,{'contender':expected_name,'configuration':'relaxed','count':4,'workers':2,'capacity':4,'reset_workers':4,'mode':'fresh'},image_ids)
    common.require_cleanup(data,[],[])
    return {'report':str(path.relative_to(ROOT)),'sha256':common.digest(path),'recovery':str(recovery.relative_to(ROOT)),
            'recovery_sha256':common.digest(recovery),'binding':report['Binding']}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('phase',choices=['qualify','paired']);parser.add_argument('--output',type=pathlib.Path,required=True);parser.add_argument('--qualification',type=pathlib.Path)
    a=parser.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
    bound=common.inputs()
    if bound['status']:raise ValueError('CleanCheckpointRequired')
    image=json.loads(common.command('docker','--host','unix:///var/run/docker.sock','image','inspect',PG))[0]
    image_ids={image['Id']};inventory=common.inventory()
    manifest={'phase':a.phase,'inputs':bound,'images':[{k:image.get(k) for k in ['Id','RepoDigests','Size','Architecture','Os']}],
              'initial_containers':inventory,'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),
              'platform':common.command('uname','-a'),'cpu':json.loads(common.command('lscpu','--json')),
              'filesystem':json.loads(common.command('findmnt','-T',str(ROOT),'-J')),
              'docker_filesystem':json.loads(common.command('findmnt','-T','/var/lib/docker','-J')),
              'storage':json.loads(common.command('lsblk','-d','-o','NAME,MODEL,SIZE,ROTA','--json')),
              'docker':json.loads(common.command('docker','--host','unix:///var/run/docker.sock','version','--format','{{json .Server}}')),
              'memory':{line.split(':')[0]:line.split(':',1)[1].strip() for line in pathlib.Path('/proc/meminfo').read_text().splitlines() if line.split(':')[0] in ['MemTotal','MemAvailable','SwapTotal','SwapFree']}}
    if a.phase=='paired':
        if a.qualification is None:raise ValueError('QualificationRequired')
        qroot=a.qualification.resolve();qmanifest=json.loads((qroot/'manifest.json').read_text());qreceipts=json.loads((qroot/'receipts.json').read_text())
        qcompletion=json.loads((qroot/'completion.json').read_text())
        if qmanifest['inputs']!=bound or len(qreceipts)!=2 or any(r['exit_code']!=0 or not r['inputs_unchanged'] or r['post_containers']!=qmanifest['initial_containers'] for r in qreceipts) or not qcompletion['all_passed']:raise ValueError('QualificationReceipt')
        for i,name in enumerate(['respawn','compiled']):
            path=qroot/f'{i:03d}-{name}'/'qualification.json'
            if qreceipts[i]['case']['contender']!=name or qreceipts[i]['report_path']!=str(path.relative_to(ROOT)) or qreceipts[i]['report_sha256']!=common.digest(path):raise ValueError('QualificationReportDigest')
        manifest['qualification']=[qualification_report(qroot/f'{i:03d}-{name}'/'qualification.json',bound,image_ids,name) for i,name in enumerate(['respawn','compiled'])]
        cases=plan();validate_plan(cases)
    else:cases=[{'contender':name,'configuration':'relaxed','capacity':4,'reset_workers':4} for name in ['respawn','compiled']]
    common.save(out/'plan.json',cases);manifest['plan_sha256']=common.digest(out/'plan.json');common.save(out/'manifest.json',manifest)
    receipts=[]
    for index,case in enumerate(cases):
        common.require_identity(bound,'BeforeCase')
        case_dir=out/f'{index:03d}-{case["contender"]}'
        argv=['dotnet',str(DLL),'qualify',case['contender'],str(case_dir),'25','relaxed'] if a.phase=='qualify' else ['dotnet',str(DLL),'run',case['contender'],'1000','4','warm',str(case_dir),'25','relaxed']
        env=dict(os.environ,TT032_RESET_WORKERS='4',TT032_CAPACITY='4',TT032_TRACE='0')
        receipt={'case':case,'command':argv,'cwd':str(ROOT),'environment':{k:env[k] for k in ['TT032_RESET_WORKERS','TT032_CAPACITY','TT032_TRACE']},'before_loadavg':pathlib.Path('/proc/loadavg').read_text().strip(),'timeout_seconds':1500}
        started=time.perf_counter()
        with open(out/f'{index:03d}.stdout.txt','w') as log:
            child=subprocess.Popen(argv,cwd=ROOT,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);receipt['pid']=child.pid
            try:code=child.wait(timeout=1500)
            except (subprocess.TimeoutExpired,KeyboardInterrupt) as error:
                receipt['interruption']=type(error).__name__;os.killpg(child.pid,signal.SIGINT)
                try:code=child.wait(timeout=60)
                except subprocess.TimeoutExpired:os.killpg(child.pid,signal.SIGKILL);code=child.wait();receipt['forced_kill']=True
        path=case_dir/('qualification.json' if a.phase=='qualify' else 'report.json')
        report=json.loads(path.read_text()) if path.exists() else None
        receipt.update(exit_code=code,process_wall_ms=(time.perf_counter()-started)*1000,after_loadavg=pathlib.Path('/proc/loadavg').read_text().strip(),post_containers=common.inventory(),report_path=str(path.relative_to(ROOT)),report_sha256=common.digest(path) if path.exists() else None,inputs_unchanged=common.inputs()==bound,report_succeeded=bool(report and report['Succeeded']))
        receipts.append(receipt);common.save(out/'receipts.json',receipts)
        print(json.dumps({'finished':index+1,'planned':len(cases),'case':case,'exit_code':code,'succeeded':receipt['report_succeeded']}),flush=True)
        if not receipt['inputs_unchanged'] or report is None or not binding_matches(report,bound):raise RuntimeError('ExperimentIdentityRejected')
        if a.phase=='qualify':
            if receipt['post_containers']!=inventory:raise RuntimeError('UnsettledResourceInventory')
            qualification_report(path,bound,image_ids,case['contender'])
        else:
            validate_run(report,case,image_ids)
            common.require_cleanup(report,receipt['post_containers'],inventory)
        if 'interruption' in receipt:raise RuntimeError('ExperimentInterrupted')
    success=all(r['exit_code']==0 and r['report_succeeded'] for r in receipts)
    common.save(out/'completion.json',{'planned':len(cases),'completed':len(receipts),'all_passed':success,'ended_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())})
    return 0 if success else 1

if __name__=='__main__':raise SystemExit(main())

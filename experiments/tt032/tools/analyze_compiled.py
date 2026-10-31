#!/usr/bin/env python3
"""TT-033 paired analysis, bound to every retained attempt."""
import argparse, hashlib, itertools, json, math, pathlib, statistics
import analyze as statistics_base
from run_compiled import ROOT,validate_plan,validate_run,qualification_report

def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()

def paired_interval(candidate,reference):
    if len(candidate)!=len(reference) or len(candidate)!=5:raise ValueError('FiveCompletePairsRequired')
    values=sorted(statistics.median(candidate[i] for i in sample)/statistics.median(reference[i] for i in sample)
                  for sample in itertools.product(range(5),repeat=5))
    return [values[math.ceil(len(values)*p)-1] for p in [.025,.975]]

def validate_qualification_evidence(manifest):
    if len(manifest['qualification'])!=2:raise ValueError('QualificationEvidenceCount')
    for name,proof in zip(['respawn','compiled'],manifest['qualification']):
        for key,sha in [('report','sha256'),('recovery','recovery_sha256')]:
            path=ROOT/proof[key]
            if not path.is_file():raise ValueError('QualificationEvidenceMissing')
            if digest(path)!=proof[sha]:raise ValueError('QualificationEvidenceDigest')
        actual=qualification_report(ROOT/proof['report'],manifest['inputs'],{i['Id'] for i in manifest['images']},name)
        if actual!=proof:raise ValueError('QualificationEvidenceMismatch')

def analyze(root):
    manifest=json.loads((root/'manifest.json').read_text());plan=json.loads((root/'plan.json').read_text());validate_plan(plan)
    validate_qualification_evidence(manifest)
    if digest(root/'plan.json')!=manifest['plan_sha256']:raise ValueError('PlanDigest')
    receipts=json.loads((root/'receipts.json').read_text());completion=json.loads((root/'completion.json').read_text())
    if [r['case'] for r in receipts]!=plan or completion['completed']!=10 or completion['planned']!=10:raise ValueError('CompleteAttemptPlan')
    reports=[];eligible=True
    for index,r in enumerate(receipts):
        path=root/f'{index:03d}-{r["case"]["contender"]}'/'report.json'
        if digest(path)!=r['report_sha256']:raise ValueError('ReportDigest')
        p=json.loads(path.read_text())
        validate_run(p,r['case'],{i['Id'] for i in manifest['images']})
        if not r['inputs_unchanged'] or r['post_containers']!=manifest['initial_containers'] or not p['CleanupCompleted'] or not p['Cleanup'] or not all(x['Absent'] for x in p['Cleanup']):raise ValueError('IdentityOrCleanup')
        for key,name in [('AssemblySha','CompetitiveBaseline.dll'),('TurboSha','TurboTest.EFCore.dll')]:
            expected=manifest['inputs']['binaries']['experiments/tt032/bin/Release/net10.0/'+name]
            if p['Binding'][key].lower()!=expected:raise ValueError('BinaryBinding')
        if p['Binding']!=manifest['qualification'][0]['binding'] or p['Binding']!=manifest['qualification'][1]['binding']:raise ValueError('QualificationBinding')
        valid=r['exit_code']==0 and p['Succeeded']
        if valid and (len(p['Samples'])!=1000 or p['SettledState']['Ready']!=4 or p['SettledState']['ResetFailures']!=0):raise ValueError('CompleteWorkflow')
        eligible=eligible and valid;reports.append((r,p))
    if completion['all_passed']!=eligible:raise ValueError('CompletionStatusMismatch')
    result={'eligible':eligible,'attempted':10,'succeeded':sum(r['exit_code']==0 and p['Succeeded'] for r,p in reports),
            'manifest_sha256':digest(root/'manifest.json'),'receipts_sha256':digest(root/'receipts.json'),
            'uncertainty':'Exact paired bootstrap ratio of medians, five independent round pairs; no hardware-population or product-wide guarantee.',
            'failures':[{'case':r['case'],'exit_code':r['exit_code'],'failures':p['Failures']} for r,p in reports if r['exit_code']!=0 or not p['Succeeded']], 'contenders':{}}
    for name in ['respawn','compiled']:
        rows=[(r,p) for r,p in reports if r['case']['contender']==name and r['exit_code']==0 and p['Succeeded']]
        if not rows:continue
        samples=[s for _,p in rows for s in p['Samples']]
        result['contenders'][name]={'timing_condition':'All five attempts passed' if len(rows)==5 else 'Conditional on successful attempts; incomplete comparison ineligible',
          'suite_ms':statistics_base.describe([p['SuiteMs'] for _,p in rows]),
          'components_ms':{key:statistics_base.describe([r['process_wall_ms'] if key=='ProcessWallMs' else p[key] for r,p in rows]) for key in ['PreparationMs','WarmupMs','TestsReturnedMs','SettlementMs','FinalAuditMs','DrainMs','CompleteMs','ProcessWallMs']},
          'throughput_tests_per_second':statistics_base.describe([1000000/p['SuiteMs'] for _,p in rows]),
          'operations':{key:{'n':len(samples),'median':statistics.median(s[key] for s in samples),'p95':statistics_base.percentile([s[key] for s in samples],.95),'maximum':max(s[key] for s in samples)} for key in ['AcquireMs','IdentitySqlMs','ApplicationMs','ReturnMs','CycleMs']},
          'resources':{'client_sampled_peak_bytes':max(s['ClientWorkingSet'] for _,p in rows for s in p['ResourceSamples']),
                       'postgres_sampled_peak_bytes':max(s['MemoryBytes'] for _,p in rows for s in p['ResourceSamples']),
                       'pg_growth_kib':[p['FinalResources']['PostgresAllocatedKiB']-p['InitialResources']['PostgresAllocatedKiB'] for _,p in rows],
                       'pg_final_kib':[p['FinalResources']['PostgresAllocatedKiB'] for _,p in rows],
                       'limitation':'Separate sampled peaks after preparation, not simultaneous aggregate or startup maxima.'},
          'plan_bytes':[p['SettledState']['PlanBytes'] for _,p in rows], 'compilation_ms':[p['SettledState']['CompilationMs'] for _,p in rows]}
    if eligible:
        c=result['contenders']['compiled']['suite_ms'];r=result['contenders']['respawn']['suite_ms'];interval=paired_interval(c['raw'],r['raw'])
        result.update(candidate_over_reference=c['median']/r['median'],paired_ratio_95=interval,paired_half_time_target_ms=r['median']/2,
                      historical_planning_target_ms=3233.3591,two_x_demonstrated=interval[1]<=.5,
                      decision='Scoped 2x threshold met; production adoption remains separate' if interval[1]<=.5 else 'Stop this candidate: scoped 2x threshold not demonstrated')
    else:result.update(two_x_demonstrated=False,decision='Stop: failed attempts make the comparison ineligible')
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('root',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
    a.output.write_text(json.dumps(analyze(a.root),indent=2)+'\n')

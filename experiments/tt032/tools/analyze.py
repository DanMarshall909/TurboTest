#!/usr/bin/env python3
"""Summarize TT-032 independent runs; operation samples are descriptive, not replicates."""
import argparse, collections, itertools, json, math, pathlib, statistics

def percentile(xs,p):
    xs=sorted(xs)
    return xs[max(0,math.ceil(len(xs)*p)-1)] if xs else None

def interval(xs):
    # Exact empirical bootstrap distribution for n<=5; no random seed or MC error.
    medians=sorted(statistics.median(sample) for sample in itertools.product(xs,repeat=len(xs)))
    return [percentile(medians,.025),percentile(medians,.975)]

def ratio_interval(numerator, denominator):
    # Independent empirical bootstrap of the ratio of medians; aggregate equal medians.
    def distribution(xs):
        return collections.Counter(statistics.median(sample) for sample in itertools.product(xs,repeat=len(xs)))
    a,b=distribution(numerator),distribution(denominator)
    weighted=sorted((x/y,n*m) for x,n in a.items() for y,m in b.items())
    total=sum(n for _,n in weighted)
    def quantile(p):
        seen=0
        for ratio,n in weighted:
            seen+=n
            if seen>=math.ceil(total*p):return ratio
    return [quantile(.025),quantile(.975)]

def describe(xs):
    return dict(n=len(xs),median=statistics.median(xs),mean=statistics.mean(xs),p95=percentile(xs,.95),maximum=max(xs),
                bootstrap_median_95=interval(xs) if len(xs)<=5 else None,raw=xs)

def cases(root):
    receipts=json.loads((root/'receipts.json').read_text())
    rows=[]
    for index,r in enumerate(receipts):
        dirs=list(root.glob(f'{index:03d}-*'))
        report=json.loads((dirs[0]/'report.json').read_text()) if dirs and (dirs[0]/'report.json').exists() else None
        rows.append((r,report,str(dirs[0]) if dirs else None))
    return rows

def tuning(root):
    groups=collections.defaultdict(list)
    for receipt,report,path in cases(root):
        c=receipt['case'];groups[c['configuration'],c['contender'],c['reset_workers']].append((receipt,report,path))
    selections={};details=[]
    for config,name in sorted({(key[0],key[1]) for key in groups}):
        results={}
        for workers in [1,4]:
            group=groups[config,name,workers]
            valid=len(group)==2 and all(r['exit_code']==0 and p and p['Succeeded'] for r,p,_ in group)
            results[workers]={'eligible':valid,'paths':[path for _,_,path in group],
                              'suite_ms':describe([p['SuiteMs'] for _,p,_ in group]) if valid else None}
        if not results[1]['eligible'] or not results[4]['eligible']:raise ValueError('IncompleteTuning:'+str((config,name)))
        one=results[1]['suite_ms']['median'];four=results[4]['suite_ms']['median']
        selected=4 if four<=.95*one else 1
        selections.setdefault(config,{})[name]=selected
        details.append({'configuration':config,'contender':name,'selected':selected,'four_over_one':four/one,'results':results})
    return selections,details

def representative(root):
    grouped=collections.defaultdict(list)
    for receipt,report,path in cases(root):
        c=receipt['case'];grouped[c['configuration'],c['count'],c['workers'],c['mode'],c['contender']].append((receipt,report,path))
    summary=[]
    for key,group in sorted(grouped.items()):
        configuration,count,workers,mode,name=key
        valid=len(group)==5 and all(r['exit_code']==0 and p and p['Succeeded'] and p['CleanupCompleted'] for r,p,_ in group)
        row=dict(configuration=configuration,count=count,workers=workers,mode=mode,contender=name,eligible=valid,attempted=len(group),succeeded=sum(r['exit_code']==0 and bool(p) and p['Succeeded'] and p['CleanupCompleted'] for r,p,_ in group),
                 paths=[path for _,_,path in group], failures=[dict(path=path,exit_code=r['exit_code'],process_wall_ms=r['process_wall_ms'],recorded_samples=len(p.get('Samples',[])) if p else 0,cleanup_completed=p.get('CleanupCompleted') if p else None,errors=p.get('Failures') if p else ['Missing report']) for r,p,path in group if r['exit_code']!=0 or not p or not p['Succeeded']])
        successful_group=[(r,p,path) for r,p,path in group if r['exit_code']==0 and p and p['Succeeded'] and p['CleanupCompleted']]
        row['timing_condition']='All five planned attempts succeeded' if valid else 'Conditional on successful attempts; incomplete cell is not ranking-eligible'
        if successful_group:
            group=successful_group
            values=[r['process_wall_ms'] if mode=='fresh' else p['SuiteMs'] for r,p,_ in group]
            row['primary_ms']=describe(values)
            row['primary_throughput_per_second']=describe([count*1000/v for v in values])
            row['components_ms']={k:describe([p[k] for _,p,_ in group]) for k in ['PreparationMs','TestsReturnedMs','SettlementMs','FinalAuditMs','SuiteMs','DrainMs','CompleteMs','ObserverInclusiveWallMs']}
            samples=[s for _,p,_ in group for s in p['Samples']]
            row['operations']={k:{'n':len(samples),'median':statistics.median(s[k] for s in samples),'p95':percentile([s[k] for s in samples],.95),'max':max(s[k] for s in samples)} for k in ['AcquireMs','IdentitySqlMs','FirstUsefulSqlMs','ApplicationMs','ReturnMs','CycleMs']}
            resource_samples=[s for _,p,_ in group for s in p['ResourceSamples']]
            row['resources']={'samples':len(resource_samples),'client_sampled_peak_bytes':max(s['ClientWorkingSet'] for s in resource_samples),
                              'container_sampled_peak_bytes':max(s['MemoryBytes'] for s in resource_samples),
                              'pg_disk_growth_kib':[p['FinalResources']['PostgresAllocatedKiB']-p['InitialResources']['PostgresAllocatedKiB'] for _,p,_ in group],
                              'pg_final_kib':[p['FinalResources']['PostgresAllocatedKiB'] for _,p,_ in group]}
            row['suite_throughput_per_second']=describe([p['UsefulThroughputPerSecond'] for _,p,_ in group])
            by_role=collections.defaultdict(list)
            for _,p,_ in group:
                roles={c['id']:('controller' if c['Name'].endswith('_controller') else 'postgres') for c in p['InitialResources']['containers']}
                for sample in p['ResourceSamples']:by_role[roles[sample['Container']]].append(sample['MemoryBytes'])
            row['resources']['individual_container_peak_bytes_by_role']={role:max(xs) for role,xs in by_role.items()}
            row['resources']['limitation']='Separate sampled peaks; not simultaneous aggregate memory. Monitoring begins after preparation.'
        summary.append(row)
    leaders=[]
    for scenario in sorted({tuple(r[k] for k in ['configuration','count','workers','mode']) for r in summary}):
        rows=[r for r in summary if tuple(r[k] for k in ['configuration','count','workers','mode'])==scenario]
        competitors=[r for r in rows if r['contender']!='turbotest' and r['eligible']]
        turbo=next(r for r in rows if r['contender']=='turbotest')
        complete=len(rows)==4 and all(r['eligible'] for r in rows)
        item=dict(zip(['configuration','count','workers','mode'],scenario));item['complete_ranking']=complete
        item['eligibility_scope']='Five completed successful representative cases only; qualification failures and API limitations remain separate constraints in report.md'
        if competitors:
            best=min(competitors,key=lambda r:r['primary_ms']['median']);target=best['primary_ms']['median']/2
            item.update(fastest_measured_competitor=best['contender'],competitor_median_ms=best['primary_ms']['median'],two_x_target_ms=target,two_x_target_bootstrap_median_95=[v/2 for v in best['primary_ms']['bootstrap_median_95']])
            if turbo['eligible']:
                current=turbo['primary_ms']['median'];item.update(turbotest_median_ms=current,turbotest_over_competitor=current/best['primary_ms']['median'],required_reduction_ms=max(0,current-target),required_reduction_percent=max(0,100*(1-target/current)),observed_target_met=current<=target,turbotest_over_competitor_bootstrap_95=ratio_interval(turbo['primary_ms']['raw'],best['primary_ms']['raw']))
        leaders.append(item)
    return {'scenarios':summary,'leaderboard':leaders,'uncertainty':'Exact empirical bootstrap median intervals across available successful independent runs (n <= 5); small n, no familywise or hardware-population guarantee. Per-operation tails are descriptive pooled samples, not independent suite replicates.'}

def main():
    p=argparse.ArgumentParser();p.add_argument('phase',choices=['tuning','representative']);p.add_argument('root',type=pathlib.Path);p.add_argument('--output',type=pathlib.Path,required=True);p.add_argument('--selections',type=pathlib.Path)
    a=p.parse_args()
    if a.phase=='tuning':
        selections,details=tuning(a.root);result={'selection_rule':'Four workers only if both cases pass and median SuiteMs <=95% of one-worker median; sequential workflows use one.','details':details}
        if a.selections:a.selections.write_text(json.dumps(selections,indent=2)+'\n')
    else:result=representative(a.root)
    a.output.write_text(json.dumps(result,indent=2)+'\n')
if __name__=='__main__':main()

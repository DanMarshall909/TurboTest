"""Postprocess the bounded TT-034 Docker controls without changing measured inputs."""
import hashlib, json, pathlib, statistics, sys
from analyze import interval

def counter(text):
    return {k:int(v) for k,v in (line.split() for line in text.splitlines())}

def pressure(text):
    return {line.split()[0]:int(line.rsplit('total=',1)[1]) for line in text.splitlines()}

def analyze(root, original):
    m=json.loads((root/'inputs.json').read_text());old=json.loads((original/'inputs.json').read_text())
    assert m['docker_controls'] and not m['smoke'] and m['profiles']=={'bridge256':{},'bridge1024':{},'host1024':{}}
    assert json.loads((root/'completed.json').read_text())=={'failures':[],'completed':True}
    assert m['executables']==old['executables'],'Workload executable changed'
    for path in ['experiments/tt034/Program.cs','experiments/tt032/Workload.cs']:
        assert m['sources'][path]==old['sources'][path],'Workload source changed'
    result={'source_head':m['head'],'original_source_head':old['head'],'same_executables':True,'runs':30,'measured_cycles':3000,
            'profiles':{},'effects':{},'uncertainty':'Five same-round pairs; exact paired bootstrap ratio of medians, 3125 resamples.',
            'memory_boundary':'RAM plus Docker default swap allowance changes together; actual swap limits not captured. Observed current/peak swap, events and pressure retained. Peaks are container-lifetime high-water marks; pressure/event deltas span application preparation, warmup and measured suite, including observation overhead.'}
    ids=[];volumes=[]
    expected={'server_version':'16.15','fsync':'off','synchronous_commit':'off','full_page_writes':'off','wal_level':'replica',
              'max_wal_senders':'10','jit':'on','autovacuum':'on','archive_mode':'off','session_replication_role':'origin',
              'default_transaction_isolation':'read committed'}
    for profile in m['profiles']:
        contenders={}
        for contender in ['respawn','emaj']:
            reports=[];receipts=[]
            for pair in range(5):
                path=root/f'{profile}-{pair}-{contender}';r=json.loads((path/'receipt.json').read_text());d=json.loads((path/'report.json').read_text())
                assert r['cleanup'] and r['exit_code']==0 and r['inputs_unchanged'] and 'failure' not in r
                assert r['report_sha256']==hashlib.sha256((path/'report.json').read_bytes()).hexdigest()
                assert (r['profile'],r['pair'],r['contender'])==(profile,pair,contender)
                assert d['Succeeded'] and d['Contender']==contender and d['Count']==100 and d['FinalAuditMs']>=0
                assert [s['Ordinal'] for s in d['Samples']]==list(range(100))
                assert all(d['Settings'][k]==v for k,v in expected.items())
                cfg=r['configuration'];assert cfg['memory']==(256 if profile=='bridge256' else 1024)*1024**2
                assert cfg['network']==('host' if profile=='host1024' else 'bridge') and cfg['nano_cpus']==0
                assert cfg['image_id']=='sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542'
                if profile=='host1024':assert r['host_binding'].startswith('127.0.0.1:')
                ids.append(r['id']);volumes.extend(r['volumes']);reports.append(d);receipts.append(r)
            times=[d['SuiteMs'] for d in reports]
            events=[];pressures=[]
            for r in receipts:
                a=counter(r['memory_after']['memory.events']);b=counter(r['memory_before']['memory.events']);events.append({k:a[k]-b[k] for k in a})
                a=pressure(r['memory_after']['memory.pressure']);b=pressure(r['memory_before']['memory.pressure']);pressures.append({k:a[k]-b[k] for k in a})
            contenders[contender]={'suite_ms':times,'median_suite_ms':statistics.median(times),
                'median_reset_ms':statistics.median(s['ResetMs'] for d in reports for s in d['Samples']),
                'event_deltas':events,'pressure_us_deltas':pressures,
                'container_lifetime_memory_peaks_bytes':[int(r['memory_after']['memory.peak']) for r in receipts],
                'container_lifetime_swap_peaks_bytes':[int(r['memory_after']['memory.swap.peak']) for r in receipts],
                'observed_swap_current_bytes':[int(r[side]['memory.swap.current']) for r in receipts for side in ['memory_before','memory_after']]}
        c=contenders['emaj']['suite_ms'];r=contenders['respawn']['suite_ms']
        result['profiles'][profile]={'contenders':contenders,'candidate_over_reference':statistics.median(c)/statistics.median(r),'ratio_of_medians_paired_95':interval(c,r)}
    for effect,alt,base in [('memory_budget','bridge1024','bridge256'),('network','host1024','bridge1024')]:
        result['effects'][effect]={}
        for contender in ['respawn','emaj']:
            c=result['profiles'][alt]['contenders'][contender]['suite_ms'];r=result['profiles'][base]['contenders'][contender]['suite_ms']
            ci=interval(c,r);result['effects'][effect][contender]={'alternative':alt,'reference':base,'ratio_of_medians':statistics.median(c)/statistics.median(r),
                'paired_95':ci,'median_difference_ms':statistics.median(c)-statistics.median(r),'clear_improvement':ci[1]<1}
    assert len(list(root.glob('*/receipt.json')))==len(set(ids))==len(volumes)==len(set(volumes))==30
    return result

if __name__=='__main__':pathlib.Path(sys.argv[3]).write_text(json.dumps(analyze(pathlib.Path(sys.argv[1]),pathlib.Path(sys.argv[2])),indent=2)+'\n')

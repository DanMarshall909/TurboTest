"""Analyze the complete TT-034 screen; reject missing, failed or mismatched cases."""
import hashlib, itertools, json, math, pathlib, statistics, sys

def interval(c, r, median_of_pair_ratios=False):
    values=sorted((statistics.median(c[i]/r[i] for i in sample) if median_of_pair_ratios
                   else statistics.median(c[i] for i in sample)/statistics.median(r[i] for i in sample))
                  for sample in itertools.product(range(5),repeat=5))
    return [values[math.ceil(len(values)*p)-1] for p in [.025,.975]]

def analyze(root):
    manifest=json.loads((root/'inputs.json').read_text())
    assert json.loads((root/'completed.json').read_text())=={'failures':[],'completed':True}
    assert not manifest['smoke']
    profiles={'relaxed':{},'minimal-wal':{'wal_level':'minimal','max_wal_senders':'0'},'no-jit':{'jit':'off'},'no-autovacuum':{'autovacuum':'off'}}
    assert manifest['profiles']==profiles
    assert manifest['image']=='postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea'
    result={'source_head':manifest['head'],'measured_cycles':4000,'runs':40,'profiles':{},
            'uncertainty':'Exact paired bootstrap ratio of medians (3125 resamples); five independent pairs per profile on one shared host.',
            'comparison_boundary':'100 sequential complete cycles, one database/worker; excludes preparation, warmup, diagnostics/final audit and container disposal.'}
    rows={};container_ids=[];volume_ids=[]
    for profile in manifest['profiles']:
        series={}
        for contender in ['respawn','emaj']:
            reports=[];receipts=[]
            for pair in range(5):
                path=root/f'{profile}-{pair}-{contender}'
                receipt=json.loads((path/'receipt.json').read_text());report=json.loads((path/'report.json').read_text())
                assert receipt['cleanup'] and receipt['exit_code']==0 and receipt['inputs_unchanged'] and 'failure' not in receipt
                assert receipt['profile']==profile and receipt['pair']==pair and receipt['contender']==contender
                assert receipt['report_sha256']==hashlib.sha256((path/'report.json').read_bytes()).hexdigest()
                assert report['Succeeded'] and report['Contender']==contender and report['Count']==100 and len(report['Samples'])==100
                assert [s['Ordinal'] for s in report['Samples']]==list(range(100))
                assert report['FinalAuditMs']>=0 and report['Qualification']
                expected={'server_version':'16.15','fsync':'off','synchronous_commit':'off','full_page_writes':'off',
                          'wal_level':'replica','max_wal_senders':'10','jit':'on','autovacuum':'on','archive_mode':'off',
                          'session_replication_role':'origin','default_transaction_isolation':'read committed'}|profiles[profile]
                assert all(report['Settings'][k]==v for k,v in expected.items())
                assert receipt['configuration']['memory']==256*1024*1024 and receipt['configuration']['nano_cpus']==0
                assert receipt['configuration']['image_id']=='sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542'
                container_ids.append(receipt['id']);volume_ids.extend(receipt['volumes'])
                reports.append(report);receipts.append(receipt)
            rows[profile,contender]=reports
            suites=[d['SuiteMs'] for d in reports]
            series[contender]={'suite_ms':suites,'median_suite_ms':statistics.median(suites),
                'median_application_ms':statistics.median(s['ApplicationMs'] for d in reports for s in d['Samples']),
                'median_reset_ms':statistics.median(s['ResetMs'] for d in reports for s in d['Samples']),
                'preparation_ms':[d['PreparationMs'] for d in reports],
                'container_preparation_seconds':[r['container_preparation_seconds'] for r in receipts],
                'observed_checkpoint_deltas':[sum(d['After']['bgwriter'][k]-d['Before']['bgwriter'][k] for k in ['checkpoints_timed','checkpoints_req']) for d in reports],
                'observed_wal_bytes_delta':[float(d['After']['wal']['wal_bytes'])-float(d['Before']['wal']['wal_bytes']) for d in reports],
                'observed_autovacuum_deltas':[sum(t['autovacuum_count'] for t in d['After']['tables'])-sum(t['autovacuum_count'] for t in d['Before']['tables']) for d in reports],
                'jit_available':[d['After']['jit_available'] for d in reports]}
        c=series['emaj']['suite_ms'];r=series['respawn']['suite_ms']
        ratio=statistics.median(c)/statistics.median(r);paired=statistics.median([a/b for a,b in zip(c,r)])
        result['profiles'][profile]={'contenders':series,'candidate_over_reference':ratio,'median_paired_ratio':paired,
                                    'ratio_of_medians_paired_95':interval(c,r),
                                    'median_pair_ratios_paired_95':interval(c,r,True),'expand':paired<=.65}
    for profile,entry in result['profiles'].items():
        if profile=='relaxed':continue
        entry['configuration_vs_relaxed']={}
        for contender in ['respawn','emaj']:
            c=entry['contenders'][contender]['suite_ms'];r=result['profiles']['relaxed']['contenders'][contender]['suite_ms']
            entry['configuration_vs_relaxed'][contender]={'ratio_of_medians':statistics.median(c)/statistics.median(r),'round_paired_95':interval(c,r)}
    result['decision']='Expand: a profile met the prespecified screen threshold' if any(d['expand'] for d in result['profiles'].values()) else 'Stop E-Maj candidate for this workload: no profile met the expansion threshold; defer production adoption'
    assert len(list(root.glob('*/receipt.json')))==40
    assert len(set(container_ids))==40 and len(volume_ids)==len(set(volume_ids))==40
    return result

if __name__=='__main__':
    pathlib.Path(sys.argv[2]).write_text(json.dumps(analyze(pathlib.Path(sys.argv[1])),indent=2)+'\n')

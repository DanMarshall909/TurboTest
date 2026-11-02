"""Complete paired analysis with timeline checks; no result-dependent case selection."""
import hashlib,itertools,json,math,pathlib,statistics,subprocess,sys

ROOT=pathlib.Path(__file__).resolve().parents[3];out=pathlib.Path(sys.argv[1]);median=statistics.median
manifest=json.loads((out/'inputs.json').read_text())
assert manifest['phase']=='measure' and manifest['modes']==['direct','awaited','background'] and manifest['delays_ms']==[0,5,20]
assert json.loads((out/'completed.json').read_text())=={'completed':True,'failures':[]} and len(list(out.glob('*/receipt.json')))==45
for path,h in manifest['sources'].items():assert hashlib.sha256(subprocess.check_output(['git','show',manifest['head']+':'+path],cwd=ROOT)).hexdigest()==h,('Source',path)
for path,h in manifest['executables'].items():assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest()==h,('Runtime',path)
rows={};settings=[];configs=[];sql=set();owned=[];volumes=[]

def occupancy(samples,suite):
    events=[]
    for s in samples:events += [(s['AcquiredMs'],1,0),(s['ApplicationEndMs'],-1,0),(s['ResetStartMs'],0,1),(s['ResetEndMs'],0,-1)]
    app=reset=app_peak=reset_peak=0;last=overlap=0.0
    for at,da,dr in sorted(events):
        if app>0 and reset>0:overlap+=at-last
        app+=da;reset+=dr;app_peak=max(app_peak,app);reset_peak=max(reset_peak,reset);last=at
        assert app>=0 and reset>=0
    assert app==reset==0 and app_peak<=4 and reset_peak<=4
    return {'wall_ms_with_test_and_reset_active':overlap,'application_concurrency_peak':app_peak,'reset_concurrency_peak':reset_peak,
            'average_application_stage_occupancy':sum(s['ApplicationMs'] for s in samples)/suite,
            'average_reset_stage_occupancy':sum(s['ResetMs'] for s in samples)/suite}

for delay,mode,pair in itertools.product([0,5,20],manifest['modes'],range(5)):
    folder=out/f'delay{delay}-{pair}-{mode}';r=json.loads((folder/'report.json').read_text());receipt=json.loads((folder/'receipt.json').read_text())
    assert receipt['cleanup'] and receipt['exit_code']==0 and receipt['inputs_unchanged'] and not receipt['fault'] and 'failure' not in receipt
    assert receipt['pair']==pair and receipt['delay_ms']==delay and receipt['mode']==r['Mode']==mode and r['DelayMs']==delay
    assert receipt['report_sha256']==hashlib.sha256((folder/'report.json').read_bytes()).hexdigest()
    assert r['Succeeded'] and not r['FaultInjection'] and r['Count']==1000 and r['TestWorkers']==4
    b=r['Batch'];n=4 if mode=='direct' else 8;assert b['ReadySlots']==r['SlotCount']==len(r['Slots'])==n
    assert [s['Id'] for s in r['Slots']]==list(range(n)) and all(s['Qualification'] and s['State']==0 for s in r['Slots'])
    sql.update(s['DeleteSql'] for s in r['Slots']);samples=b['Samples'];assert [s['Ordinal'] for s in samples]==list(range(1000))
    assert math.isfinite(b['SuiteMs']) and b['SuiteMs']>0 and 0<=b['TestsFinishedMs']<=b['SuiteMs']
    assert math.isclose(b['FinalDrainMs'],b['SuiteMs']-b['TestsFinishedMs'],abs_tol=1e-8)
    for s in samples:
        assert all(math.isfinite(v) and v>=0 for k,v in s.items() if k.endswith('Ms'))
        assert 0<=s['SlotId']<n
        assert s['RequestedMs']<=s['AcquiredMs']<=s['ApplicationEndMs']<=s['ReturnedMs']<=s['ResetStartMs']<=s['ResetEndMs']<=b['SuiteMs']
        assert s['ReturnedMs']<=s['TestFinishedMs']<=b['TestsFinishedMs']
        if mode!='background':assert s['ResetEndMs']<=s['TestFinishedMs']
    for slot in range(n):
        leases=sorted((s for s in samples if s['SlotId']==slot),key=lambda s:s['AcquiredMs'])
        assert all(a['ResetEndMs']<=z['AcquiredMs'] for a,z in zip(leases,leases[1:])),'Premature slot reuse'
    if mode!='direct':
        p=r['PoolSettlement'];assert p['Stopped'] and p['ReadySlots']==8 and p['Queued']==p['ActiveResets']==p['FailedSlots']==p['FailedSlotReadyPublications']==0
        assert b['ResetQueueHighWater']<=8 and b['ActiveResetHighWater']<=4
    c=receipt['configuration'];assert c['memory']==c['memory_swap']==1024**3 and c['nano_cpus']==0 and c['network']=='bridge'
    assert c['image_id']=='sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542'
    assert receipt['memory_after']['memory.max']=='1073741824' and receipt['memory_after']['memory.swap.max']=='0'
    settings.append(r['Settings']);configs.append(c);owned.append(receipt['id']);volumes+=receipt['volumes']
    rows[delay,mode,pair]={'report':r,'receipt':receipt,'occupancy':occupancy(samples,b['SuiteMs'])}
assert len(sql)==1 and len(set(owned))==len(set(volumes))==45
assert all(s==settings[0] for s in settings) and all(c==configs[0] for c in configs)
assert all(settings[0][k]=='off' for k in ['fsync','synchronous_commit','full_page_writes','track_io_timing','track_wal_io_timing'])
assert settings[0]['shared_preload_libraries']=='' and settings[0]['session_replication_role']=='origin' and settings[0]['default_transaction_isolation']=='read committed'

def compare(c,r):
    values=sorted(median(c[i] for i in sample)/median(r[i] for i in sample) for sample in itertools.product(range(5),repeat=5))
    ci=[values[math.ceil(p*len(values))-1] for p in [.025,.975]]
    return {'ratio_of_medians':median(c)/median(r),'paired_95':ci,'paired_ratios':[a/b for a,b in zip(c,r)],
            'interpretation':'2x supported in this screen' if ci[1]<.5 else 'gain supported in this screen' if ci[1]<1 else 'no demonstrated gain; not parity'}

result={'source_head':manifest['head'],'validated_cases':45,'validated_tests':45000,'all_resets_drained':True,'all_slot_timelines_valid':True,'delays':{},
        'bootstrap':{'unit':'paired round','resamples':3125,'statistic':'ratio of medians','quantiles':'nearest rank','intervals':'pointwise; no multiplicity adjustment'}}
for delay in [0,5,20]:
    entry={};suites={}
    for mode in manifest['modes']:
        cases=[rows[delay,mode,p] for p in range(5)];rs=[c['report'] for c in cases];samples=[s for r in rs for s in r['Batch']['Samples']]
        suites[mode]=[r['Batch']['SuiteMs'] for r in rs]
        entry[mode]={'suite_ms':suites[mode],'median_suite_ms':median(suites[mode]),'tests_per_second_at_median':1000000/median(suites[mode]),
            'final_drain_ms':[r['Batch']['FinalDrainMs'] for r in rs], 'preparation_ms':[r['PreparationMs'] for r in rs],
            'sample_medians_ms':{k:median(s[k] for s in samples) for k in ['LeaseWaitMs','ApplicationMs','ResetQueueMs','ResetMs']},
            'queue_high_water':[r['Batch']['ResetQueueHighWater'] for r in rs], 'stage_occupancy':[c['occupancy'] for c in cases],
            'max_container_lifetime_memory_mib':max(int(c['receipt']['memory_after']['memory.peak']) for c in cases)/1024**2}
    entry['background_vs_awaited_primary']=compare(suites['background'],suites['awaited'])
    entry['background_vs_direct_secondary']=compare(suites['background'],suites['direct'])
    entry['awaited_vs_direct_secondary']=compare(suites['awaited'],suites['direct'])
    result['delays'][str(delay)]=entry
(out/'summary.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result,indent=2))

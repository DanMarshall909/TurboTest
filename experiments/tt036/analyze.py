"""Analyze only complete, source-bound TT-036 phases; rounds are the sampling units."""
import hashlib,itertools,json,math,pathlib,statistics,subprocess,sys

ROOT=pathlib.Path(__file__).resolve().parents[2]
CONFIGS=['disk-logged','tmpfs-logged','disk-unlogged','tmpfs-unlogged']
ARMS=['respawn','cached']
median=statistics.median
out=pathlib.Path(sys.argv[1]);manifest=json.loads((out/'inputs.json').read_text());phase=manifest['phase']
assert phase in ['profile','measure'] and json.loads((out/'completed.json').read_text())=={'completed':True,'failures':[]}
assert manifest['configs']==CONFIGS
for path,h in manifest['sources'].items():
    assert hashlib.sha256(subprocess.check_output(['git','show',manifest['head']+':'+path],cwd=ROOT)).hexdigest()==h,('Source',path)
for path,h in manifest['executables'].items():assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest()==h,('Runtime',path)
rounds=5 if phase=='measure' else 1;count=1000 if phase=='measure' else 100;workers=4 if phase=='measure' else 1
assert len(list(out.glob('*/receipt.json')))==rounds*8
reports={};configs={};sql=set();owned=[];volumes=[]
for config,arm,pair in itertools.product(CONFIGS,ARMS,range(rounds)):
    folder=out/f'{config}-{pair}-{arm}';receipt=json.loads((folder/'receipt.json').read_text());r=json.loads((folder/'report.json').read_text())
    assert receipt['cleanup'] and receipt['exit_code']==0 and receipt['inputs_unchanged'] and 'failure' not in receipt
    assert receipt['config']==config and receipt['pair']==pair and receipt['contender']==r['Contender']==arm
    assert receipt['report_sha256']==hashlib.sha256((folder/'report.json').read_bytes()).hexdigest()
    assert r['Succeeded'] and r['Diagnostic']==(phase=='profile') and r['Unlogged']==config.endswith('-unlogged')
    assert r['Count']==count and r['Parallelism']==workers and len(r['Workers'])==workers and r['FinalAuditMs']>=0
    assert math.isfinite(r['SuiteMs']) and r['SuiteMs']>0
    assert [w['Index'] for w in r['Workers']]==list(range(workers))
    for w in r['Workers']:
        assert w['Qualification'] and len(w['Persistence'])==11
        assert all(x['relpersistence']==('u' if r['Unlogged'] else 'p') for x in w['Persistence'])
        assert [s['Ordinal'] for s in w['Samples']]==list(range(count//workers))
        assert all(math.isfinite(s[k]) and s[k]>=0 for s in w['Samples'] for k in ['OpenMs','ApplicationMs','ResetMs','CycleMs'])
        sql.add(w['CachedSql'])
    c=receipt['configuration'];assert c['memory']==c['memory_swap']==1024**3 and c['nano_cpus']==0 and c['network']=='bridge'
    assert c['image_id']=='sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542'
    assert c['mount_type']==('tmpfs' if config.startswith('tmpfs') else 'volume')
    assert len(receipt['volumes'])==(0 if config.startswith('tmpfs') else 1)
    assert receipt['memory_after']['memory.max']=='1073741824' and receipt['memory_after']['memory.swap.max']=='0'
    settings=r['Settings'];assert all(settings[k]=='off' for k in ['fsync','synchronous_commit','full_page_writes'])
    assert settings['session_replication_role']=='origin' and settings['default_transaction_isolation']=='read committed'
    assert settings['shared_preload_libraries']==('pg_stat_statements' if phase=='profile' else '')
    assert settings['track_io_timing']==settings['track_wal_io_timing']==('on' if phase=='profile' else 'off')
    key=(c,settings);assert config not in configs or configs[config]==key;configs[config]=key
    owned.append(receipt['id']);volumes+=receipt['volumes'];reports[config,arm,pair]=r
assert len(sql)==1 and len(set(owned))==len(owned) and len(set(volumes))==len(volumes)
assert all(settings==next(iter(configs.values()))[1] for _,settings in configs.values())
common=[{k:v for k,v in c.items() if k not in ['mount_type','tmpfs']} for c,_ in configs.values()]
assert all(c==common[0] for c in common)
result={'phase':phase,'source_head':manifest['head'],'validated_cases':len(owned),'validated_cycles':len(owned)*count,'configurations':{}}
if phase=='measure':
    def compare(c,r):
        values=sorted(median(c[i] for i in sample)/median(r[i] for i in sample) for sample in itertools.product(range(5),repeat=5))
        return {'ratio_of_medians':median(c)/median(r),'paired_95':[values[math.ceil(p*len(values))-1] for p in [.025,.975]],'paired_ratios':[a/b for a,b in zip(c,r)]}
    suites={}
    for config in CONFIGS:
        entry={}
        for arm in ARMS:
            rs=[reports[config,arm,p] for p in range(5)];suites[config,arm]=[r['SuiteMs'] for r in rs]
            samples=[s for r in rs for w in r['Workers'] for s in w['Samples']]
            entry[arm]={'suite_ms':suites[config,arm],'median_suite_ms':median(suites[config,arm]),'cycle_medians_ms':{k:median(s[k] for s in samples) for k in ['OpenMs','ApplicationMs','ResetMs','CycleMs']}}
        entry['cached_vs_respawn']=compare(suites[config,'cached'],suites[config,'respawn']);result['configurations'][config]=entry
    result['primary_combined_storage_effect']={a:compare(suites['tmpfs-unlogged',a],suites['disk-logged',a]) for a in ARMS}
    result['exploratory_effects']={c:{a:compare(suites[c,a],suites['disk-logged',a]) for a in ARMS} for c in CONFIGS[1:]}
    result['bootstrap']={'resamples':3125,'unit':'paired round','statistic':'ratio of medians','quantiles':'nearest rank','intervals':'pointwise; no multiplicity adjustment'}
else:
    def category(query):
        q=query.strip();lower=q.lower()
        if 'pg_stat_force_next_flush' in lower:return 'diagnostic'
        if lower.startswith('truncate table'):return 'truncate'
        if any(q.startswith('INSERT INTO "'+t+'" SELECT') for t in ['Parents','Children','Notes']):return 'seed_insert'
        if lower.startswith('select setval'):return 'sequence'
        if lower.startswith(('begin','commit')):return 'transaction_control'
        return 'application_assertions_and_other_worker_sql'
    for config in CONFIGS:
        entry={}
        for arm in ARMS:
            r=reports[config,arm,0];assert r['ProfileExtensionVersion']=='1.10'
            before=r['ProfileBefore'];after=r['ProfileAfter']
            assert before['statements_info']==after['statements_info'] and before['statements_info']['dealloc']==0
            buckets={}
            for s in r['Statements']:
                assert s['toplevel'];b=buckets.setdefault(category(s['query']),{'calls':0,'execution_ms':0,'wal_bytes':0,'block_read_ms':0,'block_write_ms':0})
                for k,field in [('calls','calls'),('execution_ms','total_exec_time'),('wal_bytes','wal_bytes'),('block_read_ms','blk_read_time'),('block_write_ms','blk_write_time')]:b[k]+=float(s[field])
            assert buckets['truncate']['calls']==100 and buckets['seed_insert']['calls']==300 and buckets['sequence']['calls']==100
            assert buckets['diagnostic']['calls']==4
            samples=r['Workers'][0]['Samples'];reset=sum(s['ResetMs'] for s in samples)
            accounted=sum(buckets[k]['execution_ms'] for k in ['truncate','seed_insert','sequence','transaction_control'])
            client={'reset_sum_ms':reset,'median_reset_ms':median(s['ResetMs'] for s in samples),'unattributed_reset_ms':reset-accounted}
            if arm=='respawn':
                assert all(s[k] is not None and math.isfinite(s[k]) and s[k]>=0 for s in samples for k in ['TruncateTransactionMs','SeedMs'])
                client|={k:sum(s[k] for s in samples) for k in ['TruncateTransactionMs','SeedMs']}
            wal_delta={k:float(after['wal'][k])-float(before['wal'][k]) for k in ['wal_records','wal_fpi','wal_bytes','wal_write','wal_sync','wal_write_time','wal_sync_time']}
            entry[arm]={'suite_ms':r['SuiteMs'],'statement_buckets':buckets,'client':client,'cluster_wal_delta':wal_delta,
                        'checkpoint_delta':sum(after['bgwriter'][k]-before['bgwriter'][k] for k in ['checkpoints_timed','checkpoints_req'])}
        result['configurations'][config]=entry
    result['limitations']='Single diagnostic case per cell, separate P1 instrumentation; statement execution excludes some commit/file work, unattributed is not .NET CPU; cluster WAL/I/O include diagnostics/background/stat-lag and are not physical disk accounting.'
(out/'summary.json').write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result,indent=2))

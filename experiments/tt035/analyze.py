"""Summarize the predeclared paired experiment, retaining rounds as sampling units."""
import hashlib, json, math, pathlib, random, statistics, sys

out=pathlib.Path(sys.argv[1])
assert json.loads((out/'completed.json').read_text())=={'completed':True,'failures':[]}
manifest=json.loads((out/'inputs.json').read_text())
assert not manifest['smoke'] and len(list(out.glob('*/receipt.json')))==10
rows=[]; samples={'respawn':[], 'cached':[]}; configs=[]; sql=set()
for pair in range(5):
    row={'pair':pair}
    for contender in ['respawn','cached']:
        folder=out/f'{pair}-{contender}'
        r=json.loads((folder/'report.json').read_text()); receipt=json.loads((folder/'receipt.json').read_text())
        assert r['Succeeded'] and r['Count']==1000 and r['Parallelism']==4
        assert receipt['cleanup'] and receipt['exit_code']==0 and receipt['inputs_unchanged'] and 'failure' not in receipt
        assert receipt['pair']==pair and receipt['contender']==r['Contender']==contender
        assert receipt['report_sha256']==hashlib.sha256((folder/'report.json').read_bytes()).hexdigest()
        assert all(w['Qualification'] and len(w['Samples'])==250 for w in r['Workers'])
        assert [w['Index'] for w in r['Workers']]==list(range(4))
        assert r['FinalAuditMs']>=0 and math.isfinite(r['SuiteMs']) and r['SuiteMs']>0
        for w in r['Workers']:
            assert [s['Ordinal'] for s in w['Samples']]==list(range(250))
            assert all(math.isfinite(s[k]) and s[k]>=0 for s in w['Samples'] for k in ['OpenMs','ApplicationMs','ResetMs','CycleMs'])
        configs.append((receipt['configuration'],r['Settings']))
        sql.update(w['CachedSql'] for w in r['Workers'])
        for w in r['Workers']:samples[contender].extend(w['Samples'])
        row[contender]=r['SuiteMs']
    row['ratio']=row['cached']/row['respawn'];rows.append(row)
assert all(c==configs[0] for c in configs) and len(sql)==1,'Matched inputs'
medians={c:statistics.median(r[c] for r in rows) for c in samples}
rng=random.Random(35035); ratios=[]
for _ in range(20000):
    picked=[rng.choice(rows) for _ in rows]
    ratios.append(statistics.median(r['cached'] for r in picked)/statistics.median(r['respawn'] for r in picked))
ratios.sort();interval=[ratios[math.ceil(p*len(ratios))-1] for p in [.025,.975]]
result={'source_head':manifest['head'],'pairs':rows,'suite_medians_ms':medians,'candidate_reference_ratio':medians['cached']/medians['respawn'],
        'paired_bootstrap_95':interval,'bootstrap':{'seed':35035,'resamples':20000,'unit':'paired round','statistic':'ratio of medians','quantiles':'nearest rank'},
        'cycle_medians_ms':{c:{k:statistics.median(s[k] for s in v) for k in ['OpenMs','ApplicationMs','ResetMs','CycleMs']} for c,v in samples.items()},
        'decision':'2x supported' if interval[1]<.5 else 'gain supported; 2x not supported' if interval[1]<1 else 'no demonstrated gain; not equivalence',
        'validated':{'runs':10,'measured_cycles':10000,'identical_configurations':True,'identical_cached_sql':True,'all_successful_and_cleaned':True}}
(out/'summary.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))

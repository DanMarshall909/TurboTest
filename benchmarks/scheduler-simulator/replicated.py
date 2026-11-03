"""Exact whole-path ensemble under a fixed, replicated qualification protocol."""

from pathlib import Path
from collections import defaultdict
import hashlib
import statistics

from reference import digest,percentile,read
from trajectory import predict as path_predict,source_identity as path_sources
from revised import assess,require,file_identity,read_trace


def source_identity():
    here=Path(__file__).resolve().parent
    return path_sources()|{name:hashlib.sha256((here/name).read_bytes()).hexdigest()
                           for name in ('replicated.py','protocol-r4.md')}


def path_metrics(fitted,mode,wait,**scenario):
    key=f'{mode}/{wait}'
    points=[]
    for run in fitted['groups'][key]:
        one=fitted|dict(groups={key:[run]})
        rows=path_predict(one,mode,wait,**scenario)
        # The primary paths are deterministic. The independent-worker probe
        # alone draws 64 paths within each source run, outside the baseline.
        points.extend(rows if scenario.get('independent_workers') else rows[:1])
    return points


def forecast(fitted):
    predictions,bounds,paths={},{},{}
    for key in sorted(fitted['groups']):
        mode,wait=key.split('/');rows=path_metrics(fitted,mode,int(wait))
        metrics=rows[0].keys()-{'calibrationRun'}
        paths[key]=rows
        predictions[key]={k:statistics.mean(row[k] for row in rows) for k in metrics}
        bounds[key]={k:[percentile([r[k] for r in rows],.05),percentile([r[k] for r in rows],.95)] for k in metrics}
    scenarios={'body-0.8':dict(body_scale=.8),'body-1.2':dict(body_scale=1.2),
               'reset-0.8':dict(reset_scale=.8),'reset-1.2':dict(reset_scale=1.2),
               'release-tail-2ms':dict(tail_us=2000),'independent-worker-records':dict(independent_workers=True)}
    sensitivities={}
    for name,scenario in scenarios.items():
        sensitivities[name]={}
        for key in predictions:
            mode,wait=key.split('/');rows=path_metrics(fitted,mode,int(wait),**scenario)
            sensitivities[name][key]={k:statistics.mean(r[k] for r in rows) for k in predictions[key]}
    return dict(predictions=predictions,predictionBounds=bounds,pathMetrics=paths,sensitivities=sensitivities,
                trainingPathCounts={k:len(v) for k,v in fitted['groups'].items()},
                fittedSha256=digest(fitted),sourceSha256=source_identity(),
                pointRule='Exact equal-weight expectation of complete replay-path metrics; not a population-median identity or a pooled p95',
                probeSeeds=[39000,39063],independentProbeDrawsPerPath=64)


def assess_cohorts(rows):
    groups=defaultdict(list)
    require(rows and len({r['name'] for r in rows})==len(rows),'CohortIdentity: unique complete observations required')
    for row in rows:groups[(row['mode'],row['waitMilliseconds'])].append(row)
    require(all(len(group)>=2 for group in groups.values()),'CohortIdentity: two initial visits required in every stratum')
    initial=[row for group in groups.values() for row in group[:2]]
    first,full=assess(initial),assess(rows)
    return dict(qualified=first['qualified'] and full['qualified'],initialTwo=first,fullCohort=full,
                initialCaseNames=[r['name'] for r in initial])


def future_cases(strata):
    result=[]
    for index in range(21):
        pair=index+7;extra=(0,100,500)[index%3]
        waits=[25,extra] if index%2==0 else [extra,25]
        for position,wait in enumerate(waits):
            modes=('awaited','background') if (index//2+position)%2==0 else ('background','awaited')
            for mode in modes:
                if (mode,wait) in strata:
                    result.append(dict(name=f'r{pair}-w{wait}-{mode}',pair=pair,mode=mode,
                                       waitMilliseconds=wait,split='validation'))
    return result


def calibration_cases(strata):
    result=[]
    waits=(0,25,100,500)
    for pair in range(7):
        modes=('awaited','background') if pair%2==0 else ('background','awaited')
        for wait in waits[pair%4:]+waits[:pair%4]:
            for mode in modes:
                if (mode,wait) in strata:
                    result.append(dict(name=f'r{pair}-w{wait}-{mode}',pair=pair,mode=mode,
                                       waitMilliseconds=wait,split='calibration'))
    return result


def check_collection(root,contract,phase):
    names=[r['name'] for r in contract['cases'] if r['split']==phase]
    progress=read(root/(phase+'-progress.json'))
    receipt=read(root/(phase+'-process.json'))
    action='calibrate' if phase=='calibration' else 'validate'
    command=receipt['command']
    require(progress['phase']==phase and progress['complete'] is True
            and type(progress['expected']) is int and progress['expected']==len(names)
            and progress['completed']==names and progress['failure'] is None,
            'CollectionSettlement: '+phase+' did not complete its exact ordered cohort')
    require(receipt['exitCode']==0 and receipt['processGroupStopped'] is True
            and receipt['deadlineExceeded'] is False and len(command)==3 and command[2]==action
            and Path(command[1]).parts[-3:]==('experiments','tt039','collect_r4.py'),
            'CollectionSettlement: '+phase+' outer collector did not complete and settle')


def fit(root,contract):
    root=Path(root)
    require(read(root/'contract.json')==contract,'ContractIdentity: collected contract differs')
    strata={(r['mode'],r['waitMilliseconds']) for r in contract['cases']}
    require(strata and {mode for mode,_ in strata}=={'awaited','background'}
            and all(('awaited',wait) in strata and ('background',wait) in strata for _,wait in strata),
            'CalibrationSplit: both modes required for every wait')
    training=[r for r in contract['cases'] if r['split']=='calibration']
    require(training==calibration_cases(strata),'CalibrationSplit: seven fresh complete calibration rounds required')
    require(contract['cases']==training+future_cases(strata),'ContractSplit: complete fixed fresh schedule required')
    settings={k:contract[k] for k in ('slots','workers','resetConcurrency','cycles')}
    require(all(type(n) is int and n>0 for n in settings.values()) and settings['cycles']>=settings['workers'],
            'ContractSplit: positive workload and capacities required')
    bound=['contract.json','source-identity.json','runtime-identity.json','image.json','measurement-window.json',
           'protocol-freeze.json','collection-freeze.json','calibration-progress.json','calibration-process.json']
    bound+=[r['name']+'/'+name for r in training for name in ('report.json','process.json')]
    identities=file_identity(root,bound)
    check_collection(root,contract,'calibration')
    groups=defaultdict(list)
    for row in training:
        receipt=read(root/row['name']/'process.json');report=read(root/row['name']/'report.json')
        require(receipt['exitCode']==0 and receipt['processGroupStopped'] and not receipt['deadlineExceeded'],
                'TraceSettlement: fresh calibration process did not complete')
        require(report['Mode']==row['mode'] and report['AddedWaitMilliseconds']==row['waitMilliseconds'],
                'TraceIdentity: fresh calibration mode/wait binding')
        trace=read_trace(report,settings)
        record=dict(name=row['name'],workers=trace['workers'],initial_us=trace['initial_us'])
        groups[f"{row['mode']}/{row['waitMilliseconds']}"].append(record)
    require(all(len(paths)==7 for paths in groups.values()),'TrainingProjection: seven complete paths required')
    require(file_identity(root,bound)==identities,'TrainingIdentity: inputs changed during fitting')
    return dict(settings=settings,groups=dict(groups),calibrationRuns=[r['name'] for r in training],
                trainingFiles=identities,contractSha256=digest(contract),sourceSha256=source_identity())


def check_fit(root,contract,fitted):
    expected=fit(root,contract)
    require(fitted.keys()<=expected.keys()|{'runtimeSeconds'} and all(fitted.get(k)==v for k,v in expected.items()),
            'FitIdentity: fresh calibration reconstruction differs')


def validate(root,contract,fitted,frozen):
    root=Path(root);check_fit(root,contract,fitted)
    expected=forecast(fitted)
    require(frozen.keys()<=expected.keys()|{'runtimeSeconds'} and all(frozen.get(k)==v for k,v in expected.items()),
            'ForecastIdentity: exact frozen ensemble differs')
    rows=[]
    phase_names=['validation-progress.json','validation-process.json']
    validation_files=file_identity(root,phase_names)
    check_collection(root,contract,'validation')
    for row in contract['cases']:
        names=[row['name']+'/'+name for name in ('report.json','process.json')]
        before=file_identity(root,names)
        report,receipt=(read(root/name) for name in names)
        require(receipt['exitCode']==0 and receipt['processGroupStopped'] and not receipt['deadlineExceeded'],
                'TraceSettlement: observed process did not complete')
        require(report['Mode']==row['mode'] and report['AddedWaitMilliseconds']==row['waitMilliseconds'],
                'TraceIdentity: observed mode/wait binding')
        trace=read_trace(report,fitted['settings'])
        predicted=frozen['predictions'][f"{row['mode']}/{row['waitMilliseconds']}"]
        rows.append(row|dict(observed=trace['observed'],predicted=predicted,observedDrain=trace['drain'],
            maxReleaseBracketMicroseconds=trace['max_release_bracket_us'],
            unmodelledObservedCosts={k:report[k] for k in ('StartupMilliseconds','PreflightMilliseconds',
                'FinalAuditMilliseconds','DisposalMilliseconds','TotalMilliseconds')}
                |dict(processWallMilliseconds=receipt['elapsedSeconds']*1000),
            signedErrors={k:predicted[k]-trace['observed'][k] for k in predicted}))
        require(file_identity(root,names)==before,'TraceIdentity: observation changed during evaluation')
        if row['split']=='validation':validation_files.update(before)
    require(file_identity(root,phase_names)=={n:validation_files[n] for n in phase_names},
            'CollectionSettlement: validation phase evidence changed during evaluation')
    return dict(gate=assess_cohorts([r for r in rows if r['split']=='validation']),runs=rows,
                validationFiles=validation_files,forecastSha256=digest(frozen),sourceSha256=source_identity())


def main():
    import argparse
    import json
    import sys
    import time
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=('fit','forecast','validate'))
    parser.add_argument('--trace-root',type=Path,required=True)
    parser.add_argument('--fit',type=Path)
    parser.add_argument('--forecast',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args()
    if args.action=='validate' and (args.fit is None or args.forecast is None):
        parser.error('validate requires --fit and --forecast')
    if args.action=='forecast' and args.fit is None:parser.error('forecast requires --fit')
    began=time.perf_counter()
    try:
        if args.output.exists():raise FileExistsError(args.output)
        contract=read(args.trace_root/'contract.json')
        if args.action=='fit':result=fit(args.trace_root,contract)
        elif args.action=='forecast':
            fitted=read(args.fit);check_fit(args.trace_root,contract,fitted);result=forecast(fitted)
        else:result=validate(args.trace_root,contract,read(args.fit),read(args.forecast))
        result['runtimeSeconds']=time.perf_counter()-began
        args.output.parent.mkdir(parents=True,exist_ok=True)
        with args.output.open('x') as output:
            output.write(json.dumps(result,indent=2,sort_keys=True,allow_nan=False)+'\n')
        qualified=result.get('gate',{}).get('qualified')
        print(json.dumps(dict(action=args.action,qualified=qualified,output=str(args.output),runtimeSeconds=result['runtimeSeconds'])))
        return 2 if qualified is False else 0
    except (OSError,ValueError,KeyError,TypeError) as failure:
        print(f'ReplicatedInput: {failure}',file=sys.stderr)
        return 1


if __name__=='__main__':
    raise SystemExit(main())

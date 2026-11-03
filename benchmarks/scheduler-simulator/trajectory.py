"""Whole-path repeated-run reference for TT-039 protocol-r3.md."""

from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import random
import statistics

from model import Work, simulate
from reference import digest, latency_metrics, percentile, read
from revised import assess, check_contract as check_r2_contract, file_identity, read_trace, require, source_identity as r2_sources


HERE = Path(__file__).resolve().parent


def source_identity():
    return r2_sources() | {name: hashlib.sha256((HERE/name).read_bytes()).hexdigest()
                          for name in ("trajectory.py", "protocol-r3.md")}


def predict(fitted, mode, wait, *, body_scale=1, reset_scale=1, tail_us=0, independent_workers=False):
    settings = fitted["settings"]
    count, workers = settings["cycles"], settings["workers"]
    results, points = [], {}
    for seed in range(39000, 39064):
        rng = random.Random(seed)
        run = rng.choice(fitted["groups"][f"{mode}/{wait}"])
        if not independent_workers and run["name"] in points:
            results.append(points[run["name"]].copy())
            continue
        jobs = [None] * count
        for worker in range(workers):
            ordinals = range(worker, count, workers)
            path = run["workers"][worker]
            require(len(path) == len(ordinals), "TrajectoryInput: complete recorded worker path required")
            records = [rng.choice(path) for _ in ordinals] if independent_workers else path
            for ordinal, record in zip(ordinals, records):
                values = record | dict(body_us=round(record["body_us"] * body_scale),
                                       reset_us=round(record["reset_us"] * reset_scale))
                if ordinal < workers:
                    values["dispatch_us"] = run["initial_us"][worker]
                jobs[ordinal] = Work(**values)
        replay = simulate(jobs, mode=mode, slots=settings["slots"], workers=workers,
                          reset_workers=settings["resetConcurrency"], release_tail_us=tail_us)
        tests, resets = replay["tests"], replay["resets"]
        metrics = latency_metrics(replay["suite_us"] / 1000,
            [(r["acquired"] - r["requested"]) / 1000 for r in tests],
            [(r["started"] - r["queued"]) / 1000 for r in resets], replay["drain_us"] / 1000)
        metrics.update(clean_starvation=statistics.mean(r["queued_clean"] for r in tests),
                       clean_queue_mean_ms=statistics.mean(r["queue_us"] for r in tests) / 1000,
                       calibrationRun=run["name"])
        if not independent_workers:
            points[run["name"]] = metrics.copy()
        results.append(metrics)
    return results


def fit(root, contract):
    root = Path(root)
    require(read(root/"contract.json") == contract, "ContractIdentity: collected contract differs")
    historical = read(root/"training/contract.json")
    check_r2_contract(historical)
    expected = [r | dict(name="training/"+r["name"], split="calibration", historicalSplit=r["split"])
                for r in historical["cases"]]
    training = [r for r in contract["cases"] if r["split"] == "calibration"]
    require(training == expected, "TrainingProjection: retain every original run and historical role")
    future = [r for r in contract["cases"] if r["split"] == "validation"]
    strata = {(r["mode"], r["waitMilliseconds"]) for r in expected}
    require(len(contract["cases"]) == len(expected)+len(future)
            and len({r["name"] for r in contract["cases"]}) == len(contract["cases"])
            and {(r["mode"],r["waitMilliseconds"]) for r in future} == strata
            and all(sorted(r["pair"] for r in future if (r["mode"],r["waitMilliseconds"]) == key) == [5,6] for key in strata),
            "ContractSplit: require fresh complete paired rounds 5 and 6")
    settings = {k:contract[k] for k in ("slots","workers","resetConcurrency","cycles")}
    require(settings == {k:historical[k] for k in settings}, "TrainingProjection: fixed workload/configuration")
    provenance = read(root/"training-provenance.json")
    names = ["contract.json","source-identity.json","runtime-identity.json","image.json"]
    names += [r["name"]+"/"+name for r in historical["cases"] for name in ("report.json","process.json")]
    require(contract["trainingArchiveSha256"] == provenance["archiveSha256"]
            and file_identity(root/"training",names) == provenance["files"], "TrainingIdentity: archived input hashes")
    require(all(read(root/name) == read(root/"training"/name) for name in ("runtime-identity.json","image.json")),
            "TrainingIdentity: native executable/image changed")
    bound = ["contract.json","source-identity.json","runtime-identity.json","image.json","training-provenance.json"]
    bound += ["training/"+name for name in names]
    identities = file_identity(root,bound)
    groups = defaultdict(list)
    for row in training:
        report = read(root/row["name"]/"report.json")
        receipt = read(root/row["name"]/"process.json")
        require(receipt["exitCode"] == 0 and receipt["processGroupStopped"] and not receipt["deadlineExceeded"],
                "TraceSettlement: training process did not complete")
        require(report["Mode"] == row["mode"] and report["AddedWaitMilliseconds"] == row["waitMilliseconds"],
                "TraceIdentity: training mode/wait binding")
        trace = read_trace(report,settings)
        groups[f'{row["mode"]}/{row["waitMilliseconds"]}'].append(
            dict(name=row["name"],workers=trace["workers"],initial_us=trace["initial_us"]))
    require(file_identity(root,bound) == identities,"TrainingIdentity: input changed during fitting")
    return dict(settings=settings,groups=dict(groups),calibrationRuns=[r["name"] for r in training],
                trainingFiles=identities,contractSha256=digest(contract),sourceSha256=source_identity())


def forecast(fitted):
    predictions, bounds, draws_by_run = {}, {}, {}
    for key in sorted(fitted["groups"]):
        mode, wait = key.split("/")
        draws = predict(fitted,mode,int(wait))
        keys = draws[0].keys()-{"calibrationRun"}
        predictions[key] = {k:statistics.median(r[k] for r in draws) for k in keys}
        bounds[key] = {k:[percentile([r[k] for r in draws],.05),percentile([r[k] for r in draws],.95)] for k in keys}
        draws_by_run[key] = dict(Counter(r["calibrationRun"] for r in draws))
    scenarios = {"body-0.8":dict(body_scale=.8),"body-1.2":dict(body_scale=1.2),
                 "reset-0.8":dict(reset_scale=.8),"reset-1.2":dict(reset_scale=1.2),
                 "release-tail-2ms":dict(tail_us=2000),"independent-worker-records":dict(independent_workers=True)}
    sensitivities = {}
    for name,scenario in scenarios.items():
        sensitivities[name] = {}
        for key in predictions:
            mode,wait = key.split("/");draws = predict(fitted,mode,int(wait),**scenario)
            sensitivities[name][key] = {k:statistics.median(r[k] for r in draws) for k in predictions[key]}
    return dict(predictions=predictions,predictionBounds=bounds,sensitivities=sensitivities,
                trainingPathCounts={k:len(v) for k,v in fitted["groups"].items()},calibrationRunDraws=draws_by_run,
                fittedSha256=digest(fitted),sourceSha256=source_identity(),seeds=[39000,39063],replicates=64,
                sampling='Uniform complete paths with original worker order. Seed selections do not increase independent source-path support.')


def check_fit(root,contract,fitted):
    expected = fit(root,contract)
    require(fitted.keys() <= expected.keys()|{"runtimeSeconds"} and all(fitted.get(k)==v for k,v in expected.items()),
            "FitIdentity: whole-path training reconstruction differs")


def validate(root,contract,fitted,frozen):
    root = Path(root);check_fit(root,contract,fitted)
    expected = forecast(fitted)
    require(all(frozen.get(k)==v for k,v in expected.items()),"ForecastIdentity: complete frozen forecast differs")
    rows,validation_files = [],{}
    for row in contract["cases"]:
        names = [row["name"]+"/"+name for name in ("report.json","process.json")]
        before = file_identity(root,names)
        report,receipt = (read(root/name) for name in names)
        require(receipt["exitCode"]==0 and receipt["processGroupStopped"] and not receipt["deadlineExceeded"],
                "TraceSettlement: observed process did not complete")
        require(report["Mode"]==row["mode"] and report["AddedWaitMilliseconds"]==row["waitMilliseconds"],
                "TraceIdentity: observed mode/wait binding")
        trace = read_trace(report,fitted["settings"])
        predicted = frozen["predictions"][f'{row["mode"]}/{row["waitMilliseconds"]}']
        rows.append(row|dict(observed=trace["observed"],predicted=predicted,observedDrain=trace["drain"],
            maxReleaseBracketMicroseconds=trace["max_release_bracket_us"],
            unmodelledObservedCosts={k:report[k] for k in ("StartupMilliseconds","PreflightMilliseconds",
                "FinalAuditMilliseconds","DisposalMilliseconds","TotalMilliseconds")}
                |dict(processWallMilliseconds=receipt["elapsedSeconds"]*1000),
            signedErrors={k:predicted[k]-trace["observed"][k] for k in predicted}))
        require(file_identity(root,names)==before,"TrainingIdentity: observation changed during evaluation")
        if row["split"]=="validation":validation_files.update(before)
    return dict(gate=assess([r for r in rows if r["split"]=="validation"]),runs=rows,
                validationFiles=validation_files,forecastSha256=digest(frozen),sourceSha256=source_identity())


def main():
    import argparse
    import sys
    import time
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action",choices=("fit","forecast","validate"))
    parser.add_argument("--trace-root",type=Path,required=True)
    parser.add_argument("--fit",type=Path)
    parser.add_argument("--forecast",type=Path)
    parser.add_argument("--output",type=Path,required=True,help="Fresh output file; existing files are preserved")
    args=parser.parse_args()
    if args.action=="validate" and (args.fit is None or args.forecast is None):
        parser.error("validate requires --fit and --forecast")
    if args.action=="forecast" and args.fit is None:parser.error("forecast requires --fit")
    began=time.perf_counter()
    try:
        if args.output.exists():raise FileExistsError(args.output)
        contract=read(args.trace_root/"contract.json")
        if args.action=="fit":result=fit(args.trace_root,contract)
        else:
            fitted=read(args.fit);check_fit(args.trace_root,contract,fitted)
            result=forecast(fitted) if args.action=="forecast" else validate(args.trace_root,contract,fitted,read(args.forecast))
        result["runtimeSeconds"]=time.perf_counter()-began
        args.output.parent.mkdir(parents=True,exist_ok=True)
        with args.output.open("x") as output:
            output.write(json.dumps(result,indent=2,sort_keys=True,allow_nan=False)+"\n")
        qualified=result.get("gate",{}).get("qualified")
        print(json.dumps(dict(action=args.action,qualified=qualified,output=str(args.output),runtimeSeconds=result["runtimeSeconds"])))
        return 2 if qualified is False else 0
    except (OSError,ValueError,KeyError,TypeError) as failure:
        print(f"TrajectoryInput: {failure}",file=sys.stderr)
        return 1


if __name__=="__main__":
    raise SystemExit(main())

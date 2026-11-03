"""Prospective, joint-trace reference for TT-039 protocol-r2.md."""

from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import random
import statistics

from model import Work, simulate
from reference import assess as assess_original, blocks, digest, error, latency_metrics, percentile, read


HERE = Path(__file__).resolve().parent


def source_identity():
    return {name: hashlib.sha256((HERE / name).read_bytes()).hexdigest()
            for name in ("model.py", "reference.py", "revised.py", "protocol-r2.md")}


def require(condition, diagnostic):
    if not condition:
        raise ValueError(diagnostic)


def read_trace(report, settings):
    """Check explicit identities/conservation before deriving any service sample."""
    slots, workers, count = (settings[k] for k in ("slots", "workers", "cycles"))
    require(report["Succeeded"] and report["CleanupCompleted"] and report["IsolationChecked"], "TraceSettlement: failed physical run")
    for state in (report["PreflightState"], report["FinalState"]):
        require(state["Ready"] == state["Reserved"] == slots and all(state[key] == 0 for key in (
            "Leased", "WaitingResets", "ExecutingResets", "ExecutingCleanup", "QueuedAcquisitions",
            "Preparing", "Quarantined", "Retiring", "Parked", "Parking", "Rewarming")),
            "TraceSettlement: physical ledger did not settle")
    require(len(report["KnownDatabases"]) == slots, "TraceIdentity: baseline database inventory")
    samples = sorted(report["Samples"], key=lambda s: s["Ordinal"])
    require([s["Ordinal"] for s in samples] == list(range(count)) and
            all(s["Worker"] == s["Ordinal"] % workers for s in samples), "TraceIdentity: closed-loop worker ordinals")
    frequency, origin = report["Frequency"], report["OriginTick"]
    require(type(frequency) is int and frequency > 0 and type(origin) is int, "TraceTiming: clock identity")
    def external(ms):
        require(isinstance(ms, (int, float)) and math.isfinite(ms) and ms >= 0, "TraceTiming: external timestamp")
        return origin + round(ms * frequency / 1000)
    def duration(end, start):
        require(end >= start - 1, "TraceTiming: reversed boundary")
        return round(max(0, end - start) * 1_000_000 / frequency)

    by_request = defaultdict(lambda: defaultdict(list))
    expected_requests = {f"measured/{i}" for i in range(count)}
    for event in report["Boundaries"]:
        request = event["Request"]
        if request is not None and request.startswith("measured/"):
            require(request in expected_requests, "TraceIdentity: unexpected measured request")
            require(type(event["Tick"]) is int, "TraceTiming: event clock")
            by_request[request][event["Stage"]].append(event)
    required = {"request", "admit", "binding", "dirty", "reset-request", "reset-start", "service-end",
                "publish", "release-before", "release-after", "pool-settled", "complete"}
    background = report["Mode"] == "background"
    require(background or report["Mode"] == "awaited", "TraceIdentity: return mode")
    if background:
        required.add("background-complete")
    records = [[] for _ in range(workers)]
    initial, previous = [None] * workers, [None] * workers
    ownership, permits, slot_databases, reset_ids = defaultdict(list), [], {}, set()
    queue_times, reset_waits, release_widths, release_befores, release_afters, completions = [], [], [], [], [], []
    for sample in samples:
        request = f'measured/{sample["Ordinal"]}'
        events = by_request[request]
        require(required <= events.keys() and events.keys() <= required | {"queued"}
                and all(len(values) == 1 for values in events.values()), "TraceEvents: missing, duplicate or unknown boundary")
        events = {key: values[0] for key, values in events.items()}
        at = {key: event["Tick"] for key, event in events.items()}
        slot = events["admit"]["Slot"]
        reset = events["reset-request"]["Reset"]
        require(slot is not None and reset is not None, "TraceIdentity: absent slot/reset identity")
        require(reset not in reset_ids, "TraceIdentity: reset operation reused")
        reset_ids.add(reset)
        require(all(event["Slot"] == slot for key, event in events.items()
                    if key not in {"request", "queued", "background-complete"})
                and all(events[key]["Reset"] == reset for key in (
                    "reset-start", "service-end", "publish", "release-before", "release-after", "pool-settled")),
                "TraceIdentity: event identity mismatch")
        database, oid = sample["Database"], sample["Oid"]
        require(report["KnownDatabases"].get(database) == oid and events["binding"]["Database"] == database
                and events["binding"]["Oid"] == oid and slot_databases.get(slot, database) == database,
                "TraceIdentity: reservation/database binding")
        slot_databases[slot] = database
        started, acquired, returning, returned = (external(sample[key]) for key in ("Started", "Acquired", "Returning", "Returned"))
        sequence = [started, at["request"], at.get("queued", at["request"]), at["admit"], at["binding"], acquired,
                    returning, at["dirty"], at["reset-request"], at["reset-start"], at["service-end"], at["publish"],
                    at["release-before"], at["release-after"], at["pool-settled"], at["complete"]]
        require(all(b >= a - 1 for a, b in zip(sequence, sequence[1:])), "TraceTiming: lifecycle boundaries")
        require(returned >= at["reset-request"] and (background or returned >= at["complete"]), "TraceTiming: return receipt")
        completed = at["background-complete"] if background else at["complete"]
        require(completed >= at["complete"] and completed <= external(report["FinalResetsFinishedMilliseconds"]),
                "TraceSettlement: abandoned wrapper")
        ownership[slot].append((at["admit"], at["publish"]))
        permits.extend([(at["reset-start"], 1), (at["release-before"], -1)])
        release_widths.append(duration(at["release-after"], at["release-before"]))
        release_befores.append(at["release-before"])
        release_afters.append(at["release-after"])
        completions.append(completed)
        clean_queue = duration(at["admit"], at["queued"]) if "queued" in at else 0
        queue_times.append(clean_queue / 1000)
        reset_waits.append(duration(at["reset-start"], at["reset-request"]) / 1000)
        worker = sample["Worker"]
        dispatch = duration(started, previous[worker] if previous[worker] is not None else external(report["SuiteStartedMilliseconds"]))
        if previous[worker] is None:
            initial[worker] = dispatch
        previous[worker] = returned
        checkout = duration(acquired, started) - clean_queue
        require(checkout >= 0, "TraceTiming: queue exceeds checkout")
        records[worker].append(dict(body_us=duration(returning, acquired), reset_us=duration(at["service-end"], at["reset-start"]),
            checkout_us=checkout, handoff_us=duration(at["reset-request"], returning),
            receipt_us=duration(returned, returning if background else at["complete"]),
            publication_us=duration(at["publish"], at["service-end"]), release_us=duration(at["release-before"], at["publish"]),
            completion_us=duration(completed, at["release-before"]), dispatch_us=dispatch if len(records[worker]) else 0))
    require(len(slot_databases) <= slots and len(set(slot_databases.values())) == len(slot_databases), "TraceIdentity: aliased slots")
    for intervals in ownership.values():
        ordered = sorted(intervals)
        require(all(current[0] >= prior[1] for prior, current in zip(ordered, ordered[1:])), "TraceOwnership: dirty-slot reuse")
    held = 0
    for _, delta in sorted(permits):
        held += delta
        require(0 <= held <= settings["resetConcurrency"], "TracePermits: impossible definite permit occupancy")
    require(held == 0, "TraceSettlement: outstanding reset permit")
    observed = latency_metrics(report["SuiteMilliseconds"], [s["Acquired"] - s["Started"] for s in samples],
                               reset_waits, report["FinalResetsFinishedMilliseconds"] - report["LastTestFinishedMilliseconds"])
    observed.update(clean_starvation=statistics.mean("queued" in by_request[f'measured/{s["Ordinal"]}'] for s in samples),
                    clean_queue_mean_ms=statistics.mean(queue_times))
    last_receipt = max(previous)
    finish = external(report["FinalResetsFinishedMilliseconds"])
    require(external(report["LastTestFinishedMilliseconds"]) >= last_receipt and finish >= last_receipt,
            "TraceSettlement: worker/receipt endpoint")
    drain = dict(physicalDrainBracket_us=[duration(max(t, last_receipt), last_receipt) for t in (max(release_befores), max(release_afters))],
                 wrapperDrain_us=duration(max(max(completions), last_receipt), last_receipt),
                 settlementTail_us=duration(finish, max(last_receipt, max(completions), max(release_afters))))
    return dict(workers=records, initial_us=initial, observed=observed, drain=drain, max_release_bracket_us=max(release_widths))


def predict(fitted, mode, wait, *, body_scale=1, reset_scale=1, tail_us=0, block_size=8):
    settings = fitted["settings"]
    count, workers = settings["cycles"], settings["workers"]
    result = []
    for seed in range(39000, 39064):
        rng = random.Random(seed)
        run = rng.choice(fitted["groups"][f"{mode}/{wait}"])
        jobs = [None] * count
        for worker in range(workers):
            ordinals = range(worker, count, workers)
            joint = blocks(run["workers"][worker], len(ordinals), block_size, rng)
            for ordinal, record in zip(ordinals, joint):
                values = record | dict(body_us=round(record["body_us"] * body_scale),
                                       reset_us=round(record["reset_us"] * reset_scale))
                if ordinal < workers:
                    values["dispatch_us"] = run["initial_us"][worker]
                jobs[ordinal] = Work(**values)
        simulated = simulate(jobs, mode=mode, slots=settings["slots"], workers=workers,
                             reset_workers=settings["resetConcurrency"], release_tail_us=tail_us)
        tests, resets = simulated["tests"], simulated["resets"]
        metrics = latency_metrics(simulated["suite_us"] / 1000,
            [(t["acquired"] - t["requested"]) / 1000 for t in tests],
            [(r["started"] - r["queued"]) / 1000 for r in resets], simulated["drain_us"] / 1000)
        metrics.update(clean_starvation=statistics.mean(t["queued_clean"] for t in tests),
                       clean_queue_mean_ms=statistics.mean(t["queue_us"] for t in tests) / 1000,
                       calibrationRun=run["name"])
        result.append(metrics)
    return result


def check_contract(contract):
    cases = contract["cases"]
    strata = {(r["mode"], r["waitMilliseconds"]) for r in cases}
    require(cases and all(type(contract[k]) is int and contract[k] > 0 for k in
                         ("slots", "workers", "resetConcurrency", "cycles"))
            and contract["cycles"] >= contract["workers"], "ContractSplit: positive workload and capacities")
    require(len({r["name"] for r in cases}) == len(cases)
            and {r["mode"] for r in cases} == {"awaited", "background"}
            and all(sorted(r["pair"] for r in cases if (r["mode"], r["waitMilliseconds"]) == key) == list(range(5)) for key in strata)
            and all(("awaited", wait) in strata and ("background", wait) in strata for _, wait in strata)
            and all(r["split"] == ("calibration" if r["pair"] < 3 else "validation") for r in cases),
            "ContractSplit: require complete whole paired rounds 0-2 calibration and 3-4 validation")


def file_identity(root, names):
    return {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in names}


def fit(root, contract):
    root = Path(root)
    check_contract(contract)
    require(read(root / "contract.json") == contract, "ContractIdentity: contract differs from collected inputs")
    calibration = [r for r in contract["cases"] if r["split"] == "calibration"]
    names = ["contract.json", "source-identity.json", "runtime-identity.json", "image.json"]
    names += [r["name"] + "/" + name for r in calibration for name in ("report.json", "process.json")]
    identities = file_identity(root, names)
    settings = {k: contract[k] for k in ("slots", "workers", "resetConcurrency", "cycles")}
    groups = defaultdict(list)
    for row in calibration:
        receipt = read(root / row["name"] / "process.json")
        require(receipt["exitCode"] == 0 and receipt["processGroupStopped"] and not receipt["deadlineExceeded"],
                "TraceSettlement: unsuccessful or live process")
        report = read(root / row["name"] / "report.json")
        require(report["Mode"] == row["mode"] and report["AddedWaitMilliseconds"] == row["waitMilliseconds"],
                "TraceIdentity: case mode/wait binding")
        trace = read_trace(report, settings)
        groups[f'{row["mode"]}/{row["waitMilliseconds"]}'].append(
            dict(name=row["name"], workers=trace["workers"], initial_us=trace["initial_us"]))
    require(file_identity(root, names) == identities, "FitIdentity: inputs changed during fitting")
    return dict(settings=settings, groups=dict(groups), calibrationRuns=[r["name"] for r in calibration],
                trainingFiles=identities, sourceSha256=source_identity(), contractSha256=digest(contract))


def assess(rows):
    extra = dict(clean_starvation=(.10, 0), clean_queue_mean_ms=(3, .25))
    require(all(key in row[side] and math.isfinite(row[side][key]) and row[side][key] >= 0
                for row in rows for side in ("observed", "predicted") for key in extra),
            "PredictionMetrics: complete finite clean-queue measurements required")
    gate = assess_original(rows)
    for stratum in gate["strata"]:
        selected = [r for r in rows if r["mode"] == stratum["mode"] and r["waitMilliseconds"] == stratum["waitMilliseconds"]]
        stratum["metrics"].update({key: error(statistics.median(r["observed"][key] for r in selected),
            statistics.median(r["predicted"][key] for r in selected), *limit) for key, limit in extra.items()})
    gate["qualified"] = gate["qualified"] and all(m["passed"] for s in gate["strata"] for m in s["metrics"].values())
    return gate


def forecast(fitted):
    predictions, bounds, draw_counts = {}, {}, {}
    for key in sorted(fitted["groups"]):
        mode, wait = key.split("/")
        draws = predict(fitted, mode, int(wait))
        keys = draws[0].keys() - {"calibrationRun"}
        predictions[key] = {k: statistics.median(d[k] for d in draws) for k in keys}
        bounds[key] = {k: [percentile([d[k] for d in draws], .05), percentile([d[k] for d in draws], .95)] for k in keys}
        draw_counts[key] = dict(Counter(d["calibrationRun"] for d in draws))
    scenarios = {"body-0.8": dict(body_scale=.8), "body-1.2": dict(body_scale=1.2),
                 "reset-0.8": dict(reset_scale=.8), "reset-1.2": dict(reset_scale=1.2),
                 "release-tail-2ms": dict(tail_us=2000), "block-size-1": dict(block_size=1)}
    sensitivities = {}
    for name, scenario in scenarios.items():
        sensitivities[name] = {}
        for key in predictions:
            mode, wait = key.split("/")
            draws = predict(fitted, mode, int(wait), **scenario)
            sensitivities[name][key] = {k: statistics.median(d[k] for d in draws) for k in predictions[key]}
    return dict(predictions=predictions, predictionBounds=bounds, sensitivities=sensitivities,
                calibrationRunDraws=draw_counts, fittedSha256=digest(fitted), sourceSha256=source_identity(),
                seeds=[39000, 39063], replicates=64)


def check_fit(root, contract, fitted):
    expected = fit(root, contract)
    require(fitted.keys() <= expected.keys() | {"runtimeSeconds"} and all(fitted.get(k) == v for k, v in expected.items()),
            "FitIdentity: calibration-only reconstruction differs")


def validate(root, contract, fitted, frozen):
    root = Path(root)
    check_fit(root, contract, fitted)
    expected = forecast(fitted)
    require(all(frozen.get(k) == v for k, v in expected.items()), "ForecastIdentity: saved forecast differs from frozen model")
    rows, validation_files = [], {}
    for row in contract["cases"]:
        names = [row["name"] + "/" + name for name in ("report.json", "process.json")]
        identities = file_identity(root, names)
        report, receipt = (read(root / name) for name in names)
        require(report["Mode"] == row["mode"] and report["AddedWaitMilliseconds"] == row["waitMilliseconds"],
                "TraceIdentity: validation case mode/wait binding")
        require(receipt["exitCode"] == 0 and receipt["processGroupStopped"] and not receipt["deadlineExceeded"],
                "TraceSettlement: validation process did not complete")
        trace = read_trace(report, fitted["settings"])
        predicted = frozen["predictions"][f'{row["mode"]}/{row["waitMilliseconds"]}']
        rows.append(row | dict(observed=trace["observed"], predicted=predicted,
            observedDrain=trace["drain"],
            maxReleaseBracketMicroseconds=trace["max_release_bracket_us"],
            unmodelledObservedCosts={k: report[k] for k in (
                "StartupMilliseconds", "PreflightMilliseconds", "FinalAuditMilliseconds", "DisposalMilliseconds", "TotalMilliseconds")}
                | dict(processWallMilliseconds=receipt["elapsedSeconds"] * 1000),
            signedErrors={k: predicted[k] - trace["observed"][k] for k in predicted}))
        require(file_identity(root, names) == identities, "FitIdentity: observation changed during validation")
        if row["split"] == "validation":
            validation_files.update(identities)
    return dict(gate=assess([r for r in rows if r["split"] == "validation"]), runs=rows,
                forecastSha256=digest(frozen), validationFiles=validation_files, sourceSha256=source_identity())


def main():
    import argparse
    import sys
    import time
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("fit", "forecast", "validate"))
    parser.add_argument("--trace-root", type=Path, required=True)
    parser.add_argument("--fit", type=Path)
    parser.add_argument("--forecast", type=Path)
    parser.add_argument("--output", type=Path, required=True, help="New file; existing output is preserved")
    args = parser.parse_args()
    if args.action == "validate" and (args.fit is None or args.forecast is None):
        parser.error("validate requires --fit and --forecast")
    if args.action == "forecast" and args.fit is None:
        parser.error("forecast requires --fit")
    started = time.perf_counter()
    try:
        if args.output.exists():
            raise FileExistsError(args.output)
        contract = read(args.trace_root / "contract.json")
        if args.action == "fit":
            result = fit(args.trace_root, contract)
        else:
            fitted = read(args.fit)
            check_fit(args.trace_root, contract, fitted)
            result = forecast(fitted) if args.action == "forecast" else validate(args.trace_root, contract, fitted, read(args.forecast))
        result["runtimeSeconds"] = time.perf_counter() - started
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x") as output:
            output.write(json.dumps(result, indent=2, sort_keys=True, allow_nan=False) + "\n")
        qualified = result.get("gate", {}).get("qualified")
        print(json.dumps(dict(action=args.action, output=str(args.output), qualified=qualified,
                              runtimeSeconds=result["runtimeSeconds"])))
        return 2 if qualified is False else 0
    except (OSError, ValueError, KeyError, TypeError) as failure:
        print(f"RevisedInput: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

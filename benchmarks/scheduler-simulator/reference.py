"""Pinned-trace fitting and held-out evaluation for the TT-039 protocol."""

import hashlib
import json
import math
from pathlib import Path
import random
import statistics

from model import Work, simulate


HERE = Path(__file__).resolve().parent
LIMITS = dict(suite_ms=(0, .10), checkout_mean_ms=(3, .25), checkout_p95_ms=(10, .25),
              reset_queue_mean_ms=(3, .25), reset_queue_p95_ms=(10, .25), starvation_proxy=(.10, 0))


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def read(path):
    return json.loads(path.read_text())


def verify_inputs(root, pins):
    root = Path(root)
    required = {"contract.json", "contract-binding.json", "source-identity.json", "runtime-identity.json"}
    required.update(row["name"] + "/" + name for row in pins["runs"]
                    for name in ["report.json", "process.json"])
    if not required <= pins["files"].keys():
        raise ValueError("InputPins: every contract, identity, report and receipt must be pinned")
    for name, expected in pins["files"].items():
        path = root / name
        if not path.is_file():
            raise ValueError(f"InputMissing: {name}")
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise ValueError(f"InputHash: {name}")
    contract = read(root / "contract.json")
    if (pins["contractSha256"] != pins["files"]["contract.json"]
            or read(root / "contract-binding.json")["sha256"] != pins["contractSha256"]):
        raise ValueError("InputPins: contract binding disagrees")
    cases = contract["cases"]
    expected = [row | dict(split="calibration" if row["pair"] < 3 else "validation") for row in cases]
    strata = {(row["mode"], row["waitMilliseconds"]) for row in cases}
    if (pins["runs"] != expected or len({row["name"] for row in cases}) != len(cases)
            or {row["mode"] for row in cases} != {"awaited", "background"}
            or any(sorted(row["pair"] for row in cases if (row["mode"], row["waitMilliseconds"]) == key)
                   != list(range(5)) for key in strata)
            or any(("awaited", wait) not in strata or ("background", wait) not in strata
                   for _, wait in strata)):
        raise ValueError("RunSplit: require complete paired rounds 0-2 calibration and 3-4 validation")
    return contract


def micros(milliseconds):
    if not isinstance(milliseconds, (int, float)) or not math.isfinite(milliseconds) or milliseconds < 0:
        raise ValueError("TraceTiming: require finite nonnegative service execution")
    return round(milliseconds * 1000)


def fit(root, pins):
    root = Path(root)
    contract = verify_inputs(root, pins)
    groups = {}
    for row in pins["runs"]:
        if row["split"] != "calibration":
            continue
        report = read(root / row["name"] / "report.json")
        samples = sorted(report["Samples"], key=lambda s: s["Ordinal"])
        workers = contract["workers"]
        if ([s["Ordinal"] for s in samples] != list(range(contract["cycles"]))
                or any(s["Worker"] != s["Ordinal"] % workers for s in samples)):
            raise ValueError("TraceSamples: require all closed-loop worker ordinals")
        start = contract["slots"] + report["Warmups"]
        resets = report["Resets"][start:start + contract["cycles"]]
        if len(resets) != len(samples):
            raise ValueError("TraceResets: require exactly one measured reset per test")
        service = dict(name=row["name"],
                       body_us=[[micros(s["Returning"] - s["Acquired"]) for s in samples if s["Worker"] == w]
                                for w in range(workers)],
                       checkout_us=[micros(s["Acquired"] - s["Started"]) for s in samples[:workers]],
                       reset_us=[micros(r["ExecutionMilliseconds"]) for r in resets])
        if row["mode"] == "background":
            service["handoff_us"] = [micros(s["Returned"] - s["Returning"]) for s in samples]
        groups.setdefault(f'{row["mode"]}/{row["waitMilliseconds"]}', []).append(service)
    return dict(inputPinsSha256=digest(pins), groups=groups,
                protocolSha256=hashlib.sha256((HERE / "protocol.md").read_bytes()).hexdigest(),
                settings={k: contract[k] for k in ["slots", "workers", "resetConcurrency", "cycles"]},
                calibrationRuns=[r["name"] for r in pins["runs"] if r["split"] == "calibration"])


def percentile(values, fraction):
    values = sorted(values)
    return values[math.ceil(fraction * len(values)) - 1]


def latency_metrics(suite_ms, checkouts, reset_waits, drain_ms):
    return dict(suite_ms=suite_ms, checkout_mean_ms=statistics.mean(checkouts),
                checkout_p95_ms=percentile(checkouts, .95),
                reset_queue_mean_ms=statistics.mean(reset_waits),
                reset_queue_p95_ms=percentile(reset_waits, .95),
                starvation_proxy=statistics.mean(t > 1 for t in checkouts), drain_ms=drain_ms)


def blocks(values, count, size, rng):
    result = []
    while len(result) < count:
        start = rng.randrange(len(values))
        result.extend(values[(start + i) % len(values)] for i in range(min(size, count - len(result))))
    return result


def predict(fitted, mode, wait, *, body_scale=1, reset_scale=1, tail_us=0, block_size=8):
    settings = fitted["settings"]
    count, workers = settings["cycles"], settings["workers"]
    result = []
    for seed in range(39000, 39064):
        rng = random.Random(seed)
        group = fitted["groups"][f"{mode}/{wait}"]
        body, checkout, reset = (rng.choice(group) for _ in range(3))
        handoff = rng.choice(fitted["groups"][f"background/{wait}"])
        bodies = [0] * count
        for worker in range(workers):
            ordinals = range(worker, count, workers)
            for ordinal, value in zip(ordinals, blocks(body["body_us"][worker], len(ordinals), block_size, rng)):
                bodies[ordinal] = round(value * body_scale)
        reset_draws = blocks(reset["reset_us"], count, block_size, rng)
        jobs = [Work(bodies[i], round(reset_draws[i] * reset_scale),
                     rng.choice(checkout["checkout_us"]), rng.choice(handoff["handoff_us"])) for i in range(count)]
        simulated = simulate(jobs, mode=mode, slots=settings["slots"], workers=workers,
                             reset_workers=settings["resetConcurrency"], release_tail_us=tail_us)
        metrics = latency_metrics(simulated["suite_us"] / 1000,
                                  [(t["acquired"] - t["requested"]) / 1000 for t in simulated["tests"]],
                                  [(r["started"] - r["queued"]) / 1000 for r in simulated["resets"]],
                                  simulated["drain_us"] / 1000)
        metrics["virtual_queue_mean_ms"] = statistics.mean(t["queue_us"] for t in simulated["tests"]) / 1000
        result.append(metrics)
    return result


def error(observed, predicted, absolute, relative):
    allowance = max(absolute, relative * observed)
    return dict(observed=observed, predicted=predicted, signedError=predicted - observed,
                relativeError=(predicted / observed - 1) if observed else None,
                allowance=allowance, passed=abs(predicted - observed) <= allowance + 1e-12)


def assess(rows):
    if not rows or any(key not in row[side] or not math.isfinite(row[side][key]) or row[side][key] < 0
                       for row in rows for side in ["observed", "predicted"] for key in LIMITS):
        raise ValueError("PredictionMetrics: require nonempty, complete finite nonnegative measurements")
    strata = []
    for mode, wait in sorted({(row["mode"], row["waitMilliseconds"]) for row in rows}):
        selected = [r for r in rows if (r["mode"], r["waitMilliseconds"]) == (mode, wait)]
        metrics = {key: error(statistics.median(r["observed"][key] for r in selected),
                              statistics.median(r["predicted"][key] for r in selected), *limit)
                   for key, limit in LIMITS.items()}
        strata.append(dict(mode=mode, waitMilliseconds=wait, metrics=metrics))
    per_run = [dict(name=r["name"], **error(r["observed"]["suite_ms"], r["predicted"]["suite_ms"], 0, .20))
               for r in rows]
    return dict(qualified=all(m["passed"] for s in strata for m in s["metrics"].values())
                and all(r["passed"] for r in per_run), strata=strata, perRunSuite=per_run)


def observed(root, row, settings):
    report = read(root / row["name"] / "report.json")
    receipt = read(root / row["name"] / "process.json")
    start = settings["slots"] + report["Warmups"]
    resets = report["Resets"][start:start + settings["cycles"]]
    samples = report["Samples"]
    metrics = latency_metrics(report["SuiteMilliseconds"],
                              [s["Acquired"] - s["Started"] for s in samples],
                              [r["PermitWaitMilliseconds"] for r in resets], report["FinalResetWaitMilliseconds"])
    costs = {key: report[key] for key in ["StartupMilliseconds", "PreflightMilliseconds",
                                        "FinalAuditMilliseconds", "DisposalMilliseconds"]}
    costs["processWallMilliseconds"] = receipt["elapsedSeconds"] * 1000
    costs["TotalMilliseconds"] = report["TotalMilliseconds"]
    return metrics, costs


def evaluate(root, pins, fitted):
    verify_inputs(root, pins)
    if (fitted["inputPinsSha256"] != digest(pins)
            or fitted["protocolSha256"] != hashlib.sha256((HERE / "protocol.md").read_bytes()).hexdigest()):
        raise ValueError("FitIdentity: fitted input pins/protocol changed")
    expected = fit(root, pins)
    if any(fitted.get(key) != value for key, value in expected.items()):
        raise ValueError("FitIdentity: saved services must reproduce calibration-only fitting")
    predictions, bounds = {}, {}
    for mode, wait in sorted({(r["mode"], r["waitMilliseconds"]) for r in pins["runs"]}):
        draws = predict(fitted, mode, wait)
        key = f"{mode}/{wait}"
        predictions[key] = {k: statistics.median(d[k] for d in draws) for k in draws[0]}
        bounds[key] = {k: [percentile([d[k] for d in draws], .05), percentile([d[k] for d in draws], .95)]
                       for k in draws[0]}
    rows = []
    for row in pins["runs"]:
        measurements, costs = observed(Path(root), row, fitted["settings"])
        predicted = predictions[f'{row["mode"]}/{row["waitMilliseconds"]}']
        rows.append(row | dict(observed=measurements, predicted=predicted, unmodelledObservedCosts=costs,
                               signedErrors={k: predicted[k] - measurements[k] for k in measurements}))
    gate = assess([r for r in rows if r["split"] == "validation"])
    sensitivities = {}
    scenarios = {"body-0.8": dict(body_scale=.8), "body-1.2": dict(body_scale=1.2),
                 "reset-0.8": dict(reset_scale=.8), "reset-1.2": dict(reset_scale=1.2),
                 "release-tail-2ms": dict(tail_us=2000), "block-size-1": dict(block_size=1)}
    for name, scenario in scenarios.items():
        sensitivities[name] = {}
        for key in predictions:
            mode, wait = key.split("/")
            draws = predict(fitted, mode, int(wait), **scenario)
            sensitivities[name][key] = {k: statistics.median(d[k] for d in draws) for k in draws[0]}
    return dict(gate=gate, runs=rows, predictionBounds=bounds, sensitivities=sensitivities,
                fittedSha256=digest(fitted), inputPinsSha256=digest(pins), seeds=[39000, 39063], replicates=64,
                sourceSha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest()
                              for p in [HERE / n for n in ["model.py", "reference.py", "protocol.md", "inputs.json"]]})


def main():
    import argparse
    import sys
    import time
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["fit", "validate"])
    parser.add_argument("--trace-root", type=Path, required=True, help="Extracted TT-038 v4 directory")
    parser.add_argument("--fit", type=Path, help="Saved calibration-only fit (required for validate)")
    parser.add_argument("--output", type=Path, required=True, help="Fresh output file; existing files are preserved")
    args = parser.parse_args()
    started = time.perf_counter()
    try:
        pins = read(HERE / "inputs.json")
        if args.action == "fit":
            result = fit(args.trace_root, pins)
        else:
            if args.fit is None:
                parser.error("validate requires --fit")
            result = evaluate(args.trace_root, pins, read(args.fit))
        result["runtimeSeconds"] = time.perf_counter() - started
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x") as output:
            output.write(json.dumps(result, indent=2, sort_keys=True, allow_nan=False) + "\n")
        qualified = result.get("gate", {}).get("qualified")
        print(json.dumps(dict(action=args.action, output=str(args.output), runtimeSeconds=result["runtimeSeconds"],
                              qualified=qualified)))
        return 2 if qualified is False else 0
    except (OSError, ValueError, KeyError) as failure:
        print(f"ReferenceInput: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

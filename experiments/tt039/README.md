# TT-039 prospective reference traces

This one-off experiment implements [protocol-r2](../../benchmarks/scheduler-simulator/protocol-r2.md).
It compiles copied production pool sources into an experimental executable.
`instrument.py` verifies exact replacement anchors and retains original/compiled
hashes and patches. No production file or public API changes. Awaited mode adds
one explicit branch joining the current return operation; it does not load the
old TT-038 awaited binary.

`TraceProbe` records bounded in-memory events without IO/serialization under
ledger locks. Request identity is captured in queued waiters; every reset gets
a GUID. Reservation identity binds to the actual database/OID. Dirty transition,
admission and publication are recorded under the ledger lock. Permit release
is bracketed before/after the actual semaphore call: a new reset may acquire
inside that interval. The reader uses the before-bound for definite occupancy
and service sampling and reports bracket width. This is a measured timing
approximation, not a fabricated exact release instant. Prepared callback and
background wrapper completion are separate from slot/permit state. Suite end
waits for wrappers as well as the physical ledger.

The collector records process receipts through the existing `scripts/verify.py`
supervisor, source/runtime/image identities, live PostgreSQL settings, Docker
resource limits and before/after inventories, plus host CPU/load/pressure samples.
It validates every successful traced case through `revised.read_trace` before
starting another case. Intentional failure controls must fail for the named
workload rule and complete cleanup. Trace-off perturbation controls intentionally
have no boundary events and do not enter fitting or validation.

On Linux with the repository's .NET SDK, cached dependencies, Docker socket and
cached PostgreSQL image, a fresh preparation uses:

```sh
python3 experiments/tt039/instrument.py artifacts/tt039-r2/pool-sources
dotnet restore experiments/tt039/PoolTrace.csproj --locked-mode
dotnet build experiments/tt039/PoolTrace.csproj --no-restore -c Release -nr:false -p:UseSharedCompilation=false -p:PoolSources="$PWD/artifacts/tt039-r2/pool-sources" -o artifacts/tt039-r2/runtime
python3 experiments/tt039/collect.py bind
python3 experiments/tt039/collect.py qualify-final
dotnet artifacts/tt039-r2/runtime/PoolTrace.dll held-control awaited artifacts/tt039-r2/qualified-held-awaited
dotnet artifacts/tt039-r2/runtime/PoolTrace.dll held-control background artifacts/tt039-r2/qualified-held-background
dotnet artifacts/tt039-r2/runtime/PoolTrace.dll probe-cost artifacts/tt039-r2/probe-cost.json
python3 experiments/tt039/collect.py perturbation
```

Use the declared competitive-baseline storage check before native builds/runs.
All outputs require fresh paths; preserve earlier attempts. Source/runtime
binding is immutable once collection starts. A local checkpoint and its compact
`collection-freeze.json` bind the exact contract, source-file hashes and native
qualification receipt. `collect.py calibrate` refuses to run without it.
Copy the committed `contract.json` into the task output before freezing.
After all calibration cases, use the [offline fit/forecast commands](../../benchmarks/scheduler-simulator/README.md).
Freeze fit/forecast/calibration-input hashes and source hashes in a committed
prediction-binding record before creating local `forecast-freeze.json` with its
checkpoint. `collect.py validate` verifies these bindings before collecting the
16 held-out runs. It never automatically fits or changes predictions.

The command paths above are this experiment's fixed output contract, not a
maintained general benchmark runner. The retained evidence and freeze records
will supply exact source/forecast identities and results. No predictive or
policy-selection claim is made until the declared gate has been evaluated.

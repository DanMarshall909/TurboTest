# Pool benchmark

Run from the repository with its pinned .NET SDK, Git and a local Docker daemon.
Only PostgreSQL is supported. Pre-pull the image so every case starts with a cached
image and a fresh owned host:

```sh
docker pull postgres:16-alpine
dotnet run -c Release --project benchmarks/TurboTest.Benchmarks -- --output artifacts/pool-benchmarks/fixed-parking --blocks 2 --samples 2 --seed 20261015 --idle-ms 2000
```

`--plan-only` validates and saves the schedule without allocating resources.
`--smoke` runs a small fixed-two/parking-two boundary check; its results are labelled
Smoke and cannot support representative performance conclusions. Normal CI runs
fast schedule/workload/statistics/binding checks and native correctness controls.
The manually dispatched `Pool performance` workflow runs the full experiment and
uploads raw results, including failures. There is no universal 50 ms CI threshold.

The full schedule repeats fixed and explicit-parking capacities 1/2/4/8. A seeded
shuffle chooses the first order; paired blocks reverse it, and later pairs rotate
positions. Every profile within a block has the same per-worker job plan. Three
homogeneous loads precede 1→8→1→idle→8, a repeated burst, sustained eight-worker
load and a final single-worker phase. With two homogeneous samples, each case has
159 timed cycles: 53 per row size. All initial slots are warm; preparation and reset
concurrency are one, each source allows four connections, administrators allow
two, and the single host has a 256 MiB memory limit. Parking closes every eligible
idle source at the explicit idle phase and retains its database reservation.

The workload inserts 100/20/10, 1,000/200/100 or 10,000/2,000/1,000 rows into related
parent/child/note tables. It updates a retained parent seed and deletes child/note
seeds. These inserted totals exclude the one mutated retained parent. Untimed
preflight checks all three sizes, seed values, sequences and simultaneous marker
isolation. Every timed operation independently checks its baseline before loading
and its actual row distribution/mutations before return. Database names/OIDs must
belong to the initial simultaneous lease set; ordinary resets and compatible
rewarm therefore prove reuse. One-slot profiles have no simultaneous second
database; their baseline check still rejects earlier-operation rows.

Each case stores `report.json`, immutable before/after source snapshots and hashes,
Git state, SDK/runtime/assembly identities, actual image ID/digests, PostgreSQL
version, resource budgets, raw samples, structured operation events and sampled
state/memory. Source and image drift rejects a run. Invalid workload calibration
is available with `--smoke --fault zero` or `--smoke --fault distribution`; it must
exit unsuccessfully and retain named diagnostics after cleanup.

`summary.json` is refreshed after every case. It reports count, conventional median
p50, nearest-rank `ceil(.95*n)` p95 and maximum in milliseconds. Phase throughput
counts completed cycles over phase wall time; total throughput includes startup,
preflight, idle, monitoring stop and final drain. Failed/incomplete cases remain
visible and the command exits nonzero. Empty timing groups have unavailable
percentiles. Raw Docker cgroup memory usage includes cache; .NET working set and
sampled peaks are retained, and peaks are lower bounds. CPU utilization is not
reported. Resource sampling stops before final host deletion; drain cost remains
explicit. A canceled caller does not waive lease, monitor or fixture cleanup.

Checkout measures the public request through lease publication. Return measures
the disposal request through its shared receipt, including context cleanup and
restoration. Cycle includes baseline verification, loading, independent workload
checks and return. Detailed queue/permit/execution/validation/recreation/rewarm
events overlap these boundaries; do not sum them or substitute slot rewarm for
request checkout. The warm 50 ms aspiration concerns verified row restoration of
the named small 130-row workload, including validation; queue, test body, startup,
recreation and drain are separate costs. Local observations are not a portable
release guarantee. Adaptive comparisons and evidence-selected policy defaults
are completed with TT-009.

The first local repeated fixed/parking experiment and its separate current
correctness gates are retained in the
[slice evidence archive](../../docs/issues/evidence/2026-10-15/pool-metrics-fixed-benchmark/manifest.json).
Its 16 cases and 2,544 measured cycles pass; later validation guards have separate
source-bound regressions. The raw historical experiment is not relabelled as
new source after those repairs.

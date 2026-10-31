# TT-032 competitive baseline

Reproducible discovery experiment for [TT-032](https://github.com/DanMarshall909/TurboTest/issues/25).
This is an isolated experiment, outside the maintained product and correctness
suite. It makes no product readiness or universal speed claim. GitHub owns live
acceptance and status.

## Protocol fixed before measurement

Dan confirmed Linux/WSL2 is acceptable on 2026-10-31. Native Windows is not
required for this claim. The available measurement host is native Linux x64.
Linux measurements cannot establish Windows or WSL2 performance.

The claim is complete elapsed time at most half the fastest applicable measured
competitor **for each scenario**, including required restoration and settled
capacity. No cross-scenario average establishes 2×. The current TurboTest public
API at `221493ba28fa0f2d152563766dcafd3652796994` is a candidate, not a constraint.

Compare actual IntegreSQL, actual Respawn plus seed/sequence restoration, native
PostgreSQL templates with bounded preparation, and TurboTest. All use the same
cached PostgreSQL 16 image on the same host and ordinary persistent Docker
storage. The `durable` configuration leaves server `fsync`, synchronous commit
and full-page writes enabled. A separate `relaxed` configuration disables all
three after initial fixture preparation, consistently for all contenders; the
settings change and reload are included in preparation. This is deliberately
disposable test storage, not crash durability. Initial preparation remains
identical to the durable path because TurboTest does not expose server launch
flags. No RAM-backed-storage or filesystem-clone claim is made. pgtestdb is prior art for
the native control, not a measured fifth implementation. Transaction rollback
cannot satisfy committed multi-connection writes and is outside this claim.

Workflows: one test and 20 affected tests sequentially with one workload slot;
a sustained suite with four workers and four workload slots; freshly prepared
and already prepared fixtures. The sustained durable suite has 100 tests and
the relaxed suite has 1,000. Preliminary durable template runs spent about
0.6 seconds per database deletion, making repeated 1,000-test durable cases
impractical for this discovery. Both sustained cases exhaust initial capacity
many times; the 100-test result does not establish 1,000-test durable behavior. Fresh includes owned service startup, migrations, seeds and initial
capacity. Warm starts at ready capacity and ends at the same ready capacity,
including replenishment; final service drain is also recorded, with a separate
all-cost total. A warmed runtime/image cache is explicit; this is not an SDK
installation, image-download or cold-OS-cache benchmark.

Use three related tables and deterministic realistic seeds (32 parents, 64
children, 128 notes). Each test commits updates, deletes and 100/20/10 inserts
through two simultaneously open physical connections; an independent connection
observes the commits. All competitors run the same SQL and independent checks
of exact baseline values, relationships, sequence state, resulting rows and
markers. Four-worker cases check separate database identities and marker
isolation. Schema/replacement guarantees and privileges are reported separately;
a data-only reset is never ranked as a general schema reset.

Resource allowance: at most four workload databases, four connections per
database, four preparation/admin connections plus one observation connection,
a 256 MiB PostgreSQL container, and the same host CPU allocation. Sequential
workflows prepare only one slot. Matched tuning compares one versus four
preparation/restoration workers; native initial cloning is bounded by the same
selected concurrency. IntegreSQL limits pool workers but does not set a hard
Go `database/sql` connection ceiling. Qualification records held workloads and
service sessions and checks this allowance; sampled observations are not proof
of a hard intrinsic IntegreSQL connection bound. IntegreSQL's additional controller is separately
bounded and measured; client memory and actual connection/database counts are
recorded. CPU access is the same host allocation for all cases. Record any tool
limits that prevent exact matching. Run contenders serially; retain ambient-load
observations and do not claim exclusive hardware.

Use a seeded counterbalanced order, at least five independent complete rounds,
and retain every failure. Select restoration concurrency per contender and
durability configuration using the median warm-suite time from two matched
pilot rounds; use four only if it is at least 5% faster and both runs pass.
Sequential scenarios use one restoration worker. Freeze the selection before
representative rounds; never choose from the representative results. Report per-run wall times, median, p95 and bootstrap
uncertainty across independent runs; operation samples are not independent suite
replicates. Report preparation, acquisition, first useful SQL, application work,
return, settlement and drain separately without adding overlapping intervals.
Sample memory and database/storage growth; peaks are lower bounds. Final cleanup
receipts identify exact owned containers/volumes and verify removal without
touching unrelated resources.

Primary ranking metric: fresh scenarios use complete process wall time from
process launch through exit, including startup, preparation, assertions,
restored-capacity audit, resource observations, report writing and final drain.
Warm scenarios use `SuiteMs`: first request through all returns, replenishment
and the exact restored-capacity audit. Warm service preparation/warmup and
final destruction are separately retained in complete totals. `FeedbackMs`
and `CompleteMs` are component sums and are not substitutes for process wall
time. `IdentitySqlMs` measures the identity-query boundary; `FirstUsefulSqlMs`
measures request start through the first baseline-query result. These overlap
other intervals and must not be summed.

The native, Respawn and IntegreSQL adapters destroy their entire owned
PostgreSQL host at final drain. IntegreSQL's owned controller is removed first;
per-template deletion would duplicate whole-host cleanup. TurboTest uses its
public factory disposal. None of these paths deletes a shared/unowned service.

Before representative timing, qualify ordinary failures, cancellation, schema
replacement/stale connections, reset failure and recovery, reordered repetition,
resource bounds, fresh migrations and cached baseline construction. Corrupt seed,
sequence and workload results deliberately and require named correctness failures.
Failed or unsupported probes remain visible and constrain applicable rankings.
Respawn data reset does not discover schema changes: the schema-replacement
probe explicitly signals invalidation and uses native template replacement.
Thus the primary data-only leaderboard is not a ranking of automatic schema
change detection. All connections used by tests are lease-owned; retained
physical connection references must be closed and old backend PIDs absent after
replacement. Unowned connections that a test refuses to close are outside this
common lifetime contract.

The versioned SQL migration creates the four tables and records version 1;
reapplication is idempotent. This is a SQL migration, not EF's migration runner.
Fresh/cached construction counters and independent schema/seed checks are retained.
IntegreSQL auto-recreation is used without manual recreation, avoiding an
upstream race concern. A physical connection is pinned throughout each lease.
Minimum lifetime is 25 ms, retry delay 5–25 ms; correctness and replacement
probes qualify this local configuration, not arbitrary remote connection latency.
Debug `ready` events bind every issued ID to its next published generation.
Settlement requires that vector, no active server SQL, and exact baseline checks
of every workload slot. The final upstream bookkeeping after `ready` does no
more database work; raw logs retain the observable boundary. Full trace/request
logging is disabled for representative measurement.

Source identities bind the whole experiment, including gaps between cases.
The supervisor rejects source/binary/SDK drift and retains failures before
stopping on an identity/resource mismatch. It keeps one immutable plan and a
receipt for every attempt; there are no automatic case retries. This directly
addresses the relevant TT-013/014/016 evidence concerns without claiming those
separate maintained-benchmark tickets are resolved.

## Decision

The final report will distinguish measured results, upstream published claims
and hypotheses. For each applicable workflow it will name the measured winner,
the half-time target, TurboTest's required reduction and one smallest follow-up
experiment with an explicit success and stop/pivot condition. Neither this
protocol nor a fast acquisition microbenchmark establishes the product's 2× goal.

## Commands

```sh
dotnet restore experiments/tt032/CompetitiveBaseline.csproj --locked-mode
dotnet build experiments/tt032/CompetitiveBaseline.csproj -c Release --no-restore
# Qualification requires capacity 4; run contenders serially.
TT032_RESET_WORKERS=4 dotnet experiments/tt032/bin/Release/net10.0/CompetitiveBaseline.dll qualify respawn artifacts/tt032/qualification-example 25
python3 experiments/tt032/tools/run_experiment.py tune --rounds 2 --output artifacts/tt032/tuning
# After freezing the evidence-backed selections at a clean checkpoint:
python3 experiments/tt032/tools/run_experiment.py representative --rounds 5 --selections experiments/tt032/selections.json --output artifacts/tt032/representative
```

The supervisor requires a clean committed source tree and existing built binaries.
Run one producer at a time on the Docker daemon. A case interruption retains its
exit status and any partial report; forced termination is not cleanup evidence.

Upstream contracts checked against [IntegreSQL v1.1.0](https://github.com/allaboutapps/integresql/tree/v1.1.0),
[Respawn v7.0.0](https://github.com/jbogard/Respawn/tree/v7.0.0),
[pgtestdb v0.1.1](https://github.com/peterldowns/pgtestdb/tree/v0.1.1), and
[PostgreSQL 16 CREATE DATABASE](https://www.postgresql.org/docs/16/sql-createdatabase.html).
The controller and PostgreSQL images are pinned by digest in the harness.

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
storage. Server `fsync` and synchronous commit remain enabled. No RAM-backed,
relaxed-durability or filesystem-clone claim is made. pgtestdb is prior art for
the native control, not a measured fifth implementation. Transaction rollback
cannot satisfy committed multi-connection writes and is outside this claim.

Workflows: one test, 20 affected tests, 1,000 sustained tests; sequential and
four-worker suite execution where useful; freshly prepared and already prepared
fixtures. Fresh includes owned service startup, migrations, seeds and initial
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

Resource ceiling: four workload databases, four pooled connections per database,
two preparation/admin connections where supported, one preparation/reset worker,
256 MiB PostgreSQL container. IntegreSQL's additional controller is separately
bounded and measured; client memory and actual connection/database counts are
recorded. CPU access is the same host allocation for all cases. Record any tool
limits that prevent exact matching. Run contenders serially; retain ambient-load
observations and do not claim exclusive hardware.

Use a seeded counterbalanced order, at least five independent complete rounds,
and retain every failure. Report per-run wall times, median, p95 and bootstrap
uncertainty across independent runs; operation samples are not independent suite
replicates. Report preparation, acquisition, first useful SQL, application work,
return, settlement and drain separately without adding overlapping intervals.
Sample memory and database/storage growth; peaks are lower bounds. Final cleanup
receipts identify exact owned containers/volumes and verify removal without
touching unrelated resources.

Before representative timing, qualify ordinary failures, cancellation, schema
replacement/stale connections, reset failure and recovery, reordered repetition,
resource bounds, fresh migrations and cached baseline construction. Corrupt seed,
sequence and workload results deliberately and require named correctness failures.
Failed or unsupported probes remain visible and constrain applicable rankings.

## Decision

The final report will distinguish measured results, upstream published claims
and hypotheses. For each applicable workflow it will name the measured winner,
the half-time target, TurboTest's required reduction and one smallest follow-up
experiment with an explicit success and stop/pivot condition. Neither this
protocol nor a fast acquisition microbenchmark establishes the product's 2× goal.

# TT-036 PostgreSQL storage and reset profiling

Isolated storage preparation for [TT-036](https://github.com/DanMarshall909/TurboTest/issues/33), accepted after TT-035 found no reliable gain from the combined reset command. No production backend or readiness claim. Uses the prior fixed 224-row seed, three relational tables and committed multi-connection workload.

Dan subsequently clarified that the intended design is a ready database pool whose resets run after test return while new tests receive other clean databases. That scheduling experiment now owns TT-036's primary goal. The eight-cell storage smoke qualification completed; the diagnostic and40-case performance matrix described below remain unexecuted plans, not results. Preserve this preparation and its attempts for reuse. See `pipeline/README.md` for the active scheduling experiment.

Both arms discover Respawn 7.0.0's table plan once. Reference calls actual ResetAsync then SeedSql. Candidate executes cached BEGIN + the same TRUNCATE CASCADE + the same SeedSql + COMMIT. Packaging and transaction scope differ together. Both retain one admin connection per database and pool three application connections. No per-cycle baseline/schema scan. Complete measured cycles include app connection leases, mutations, independent observer assertions, connection returns and synchronous reset. Final reset is included; setup, three qualification cycles, five warmup cycles, final baseline audit and disposal are separate.

Four configurations: disk/logged, tmpfs/logged, disk/unlogged, tmpfs/unlogged. PG16 image pinned by digest; same bridge network, 1GiB memory, explicit zero swap and unlimited CPU quota. Tmpfs replaces the image's data volume, has a768MiB filesystem cap, and counts inside the same1GiB memory limit. Both arms disable fsync, synchronous_commit and full_page_writes. No other tuning differences in the primary comparison. Actual mount type/filesystem, table/index/identity-sequence persistence and server settings are asserted and recorded. Logged and unlogged fixtures both qualify FK, primary-key and NOT NULL rejection plus exact seed/sequence restoration and distinct committed sessions. User triggers and CHECK constraints are not specifically exercised; trigger/replication behavior is not disabled. Fixed owned schema and no concurrent use of a resetting worker are required.

## Separate diagnostics

One100-cycle/one-worker case for each configuration and contender. Only these cases load pg_stat_statements1.10 and enable I/O/WAL timing; query IDs on, top-level statements and utility tracking on, planning tracking off. Primary cases assert this instrumentation is off/unloaded.

After warmup, flush each actual app/admin backend and the root collector with pg_stat_force_next_flush in completed autocommit commands. Reset statement statistics only inside the owned disposable cluster, clear the collector snapshot and capture before counters. After the timed suite, flush those backends again and capture after counters and worker-database statement entries before the final baseline audit. Verify statement eviction/reset metadata and expected100truncate/300seed-insert/100sequence calls. Four recorded worker flush SELECTs are diagnostics, excluded from application/reset attribution.

Respawn diagnostic samples time the complete ResetAsync transaction separately from seed restoration, without changing their SQL or transaction shape. Cached remains one command; statement statistics provide partial server-side categories. The diagnostic instrumentation affects timing, so these results cannot substitute for the primary benchmark.

**Statement timing is not a complete transaction decomposition.** pg_stat_statements' utility hook wraps ProcessUtility; COMMIT's real transaction completion follows it and can perform pending file deletions. The difference between client whole-reset time and reset-statement execution totals is unattributed, containing transaction completion, protocol, scheduling, planning and other work; it is not a measurement of .NET overhead. Statement WAL describes generation inside recorded executor/hook intervals, not complete transaction WAL or physical writes. TRUNCATE rows=0 does not mean no data was removed.

pg_stat_wal/pg_stat_io are cluster-wide and cannot filter worker databases. Worker/root flushes reduce setup contamination, but background counters may lag and diagnostic queries add some activity. These are supporting observations, not complete physical-I/O or CPU accounting. Cgroup CPU/I/O/memory observations span preparation through process disposal, not just SuiteMs. Memory peaks are lifetime high-water marks. With fsync off, zero WAL-sync timing is expected. UNLOGGED TRUNCATE still creates and WAL-logs initialization forks, so zero reset WAL is not expected.

Primary sources: [statistics](https://www.postgresql.org/docs/16/monitoring-stats.html), [pg_stat_statements](https://www.postgresql.org/docs/16/pgstatstatements.html), [utility accounting](https://github.com/postgres/postgres/blob/REL_16_15/contrib/pg_stat_statements/pg_stat_statements.c#L1123-L1198), [pending file deletion](https://github.com/postgres/postgres/blob/REL_16_15/src/backend/access/transam/xact.c#L2350-L2359), [unlogged initialization fork](https://github.com/postgres/postgres/blob/REL_16_15/src/backend/access/heap/heapam_handler.c#L607-L623), [unlogged tables/sequences](https://www.postgresql.org/docs/16/sql-createtable.html#SQL-CREATETABLE-UNLOGGED).

## Primary comparison and stopping rule

Five rounds × four configurations × two contenders =40cases/40,000cycles. Each case has four independent databases/workers and250cycles per worker. Fresh container per case. Rotate configuration order each round, alternate contender order across rounds. No extra result-dependent rounds. Abort on correctness failure, drift or unproven cleanup and retain failed attempts.

Primary storage contrasts: tmpfs/unlogged versus disk/logged separately for each contender. Other settings and matched contender contrasts are exploratory. Ratio of suite medians; exact paired bootstrap over all3,125ordered resamples of the five rounds, nearest-rank95% interval. Upper bound below1 supports a gain in this screen, below0.5 supports2× for that particular contrast. Intervals are pointwise, not adjusted for multiple comparisons. Five pairs on one shared host do not establish a universal guarantee. Gains versus ordinary storage are distinct from beating equally tuned Respawn. Pooled cycle medians are descriptive, not independent replicates or additive suite components.

## Reproduction and evidence

```sh
dotnet build experiments/tt036/StorageScreen.csproj -c Release
# Copy all runtime DLL/JSON files to a new retained artifacts/tt036/runtime-<version> folder.
# Never overwrite a version used for an attempt. Freeze source in a checkpoint before profiling/measurement.
python3 experiments/tt036/run.py --phase smoke --runtime artifacts/tt036/runtime-<version> --output artifacts/tt036/smoke-<attempt>
python3 experiments/tt036/run.py --phase profile --runtime artifacts/tt036/runtime-<version> --output artifacts/tt036/profile
python3 experiments/tt036/analyze.py artifacts/tt036/profile
python3 experiments/tt036/run.py --phase measure --runtime artifacts/tt036/runtime-<version> --output artifacts/tt036/measure
python3 experiments/tt036/analyze.py artifacts/tt036/measure
```

Retain every source version, runtime byte set, manifest, attempt, report, receipt and initial/final resource inventory. Smoke snapshots identify their source attempt, not an identically numbered runtime folder. Analysis reads frozen source from Git and checks retained runtime hashes. No broad Docker/cache cleanup; remove only exact owned containers and their anonymous volumes, and verify absence. Tmpfs cases own no anonymous data volume. Builds/evidence remain within the storage budget.

Separate-server topology and a queue of spare clean databases resetting after test completion are unmeasured alternatives, outside this matrix. The latter may overlap reset with useful work; it requires pool capacity, bounded reset throughput and final drain accounting. Current TurboTest disposal waits for reset, and this harness does too.

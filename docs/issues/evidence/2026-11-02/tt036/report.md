# Background resets improved complete suite time

Investigation for [TT-036](https://github.com/DanMarshall909/TurboTest/issues/33). Returning a database lease before its reset finishes lets the next test use another clean database. With the same eight slots, four test workers and four reset workers, this reduced complete suite time at all three tested durations. **Adopt this scheduling direction for the next bounded product experiment; defer production adoption.** The evidence supports the user's intended design, without demonstrating 2× performance or a general guarantee.

Each cell is the median of five 1,000-test suites. All final resets and restoration of ready capacity are included. The added delay is synthetic non-database waiting while holding a lease, in addition to real committed database work; it is not CPU work or a measured real application suite.

| Added wait per test | Awaited pool | Background pool | Lower suite time | Background / awaited, paired 95% interval |
| --- | ---: | ---: | ---: | --- |
| 0 ms | 5,035.34 ms | 4,053.57 ms | 19.5% | 0.8050 [0.6919, 0.8333] |
| 5 ms | 6,922.34 ms | 4,251.26 ms | 38.6% | 0.6141 [0.4270, 0.8109] |
| 20 ms | 10,963.93 ms | 7,611.11 ms | 30.6% | 0.6942 [0.5842, 0.7497] |

Background wins all five paired rounds at each duration. Throughput at the suite median increases from 198.6 to 246.7, 144.5 to 235.2, and 91.2 to 131.4 tests/second respectively. Every interval's upper endpoint is below 1, but none is below 0.5. These are pointwise intervals without multiplicity adjustment: exact paired bootstrap over all 3,125 ordered resamples of five rounds, ratio of medians, nearest-rank endpoints. Five rounds on one shared host provide limited resolution. No failed measured cases, excluded observations, replacement rounds or result-dependent extension occurred.

## What changed in the experiment

The [isolated harness](../../../../../experiments/tt036/pipeline/README.md) compares three modes using actual Respawn 7.0.0 plus the same seed/sequence restoration. Direct mode has four fixed worker databases. Both pooled modes have eight database slots, bounded ready/dirty queues and four reset workers; the only scheduling switch is whether the test worker waits for its return ticket. No production code, maintained tests, public API or workflow changed.

Each test uses three application connections, commits relational mutations and checks them through an independent observer. All application connections close before reset starts. A returned slot is queued immediately, and another clean slot can be leased. Reset successfully completes TRUNCATE and seed restoration before publication as Ready. Per-test timestamps and slot transitions verify this ownership ordering. All 15,000 background tests finished before their own reset; no awaited/direct test did. Final background drains of **8.25–212.72 ms** are included in the primary suite metric.

The current product's `TtDatabase.DisposeCoreAsync`, `TtPreparedPool.ReturnAsync` and `TtSlotPool.ReturnAsync` already support a database pool, but disposal waits for reset. The experiment isolates the scheduling change the user intended. It does not implement the production lifetime, cancellation or error-reporting contract.

All modes cache Respawn's discovered plan once, ignoring `SchemaVersion`, and execute the same SQL:

```sql
truncate table "public"."Notes","public"."Children","public"."Parents" cascade;
```

The existing seed restores 32 Parents, 64 Children and 128 Notes, and sequence states 1000/2000/3000 with `is_called=true`. Reset and seeding remain separate commands in all modes. There is no per-reset baseline scan or hash. Qualification, 20 warmup tests, final baseline audit and disposal remain outside suite timing. Ordinary application assertions and connection leases/returns remain inside it.

## Secondary direct-Respawn comparison and resource cost

| Added wait | Direct four-slot median | Background / direct, paired 95% interval | Awaited / direct, paired 95% interval |
| --- | ---: | --- | --- |
| 0 ms | 5,867.56 ms | 0.6908 [0.6307, 0.8474] | 0.8582 [0.7857, 1.0367] |
| 5 ms | 6,444.82 ms | 0.6596 [0.6345, 0.9177] | 1.0741 [1.0616, 1.4859] |
| 20 ms | 11,269.58 ms | 0.6754 [0.6435, 0.7242] | 0.9729 [0.9021, 1.1014] |

Background also improves on direct Respawn here, but that comparison changes topology and capacity: eight databases rather than four, and approximately 33 rather than 17 prepared database sessions including root/admin/application pools. Its benefit cannot be attributed only to the return switch. Awaited pooling was demonstrably slower than direct at 5 ms, losing all five pairs. The other awaited/direct intervals do not demonstrate a difference or equivalence. General Respawn parity when reset-bound is not established.

Maximum observed container-lifetime memory was **278.64 MiB direct, 390.08 MiB awaited and 390.13 MiB background**. These are high-water observations including preparation, not suite-only peaks or the total host/.NET memory cost. Both pooled modes use the same resource configuration. Slot creation/qualification also costs time:

| Added wait | Direct preparation median | Awaited preparation median | Background preparation median |
| --- | ---: | ---: | ---: |
| 0 ms | 1,155.72 ms | 3,143.70 ms | 3,332.19 ms |
| 5 ms | 1,922.56 ms | 2,746.59 ms | 3,256.36 ms |
| 20 ms | 1,048.60 ms | 2,272.84 ms | 1,716.73 ms |

These descriptive setup figures exclude Docker startup (retained separately) and are not paired setup-performance claims. The headline is steady suite time with already prepared slots, not startup-to-disposal time for a tiny suite.

Background sample medians reveal the changing bottleneck:

| Added wait | Application stage | Reset stage | Wait for clean slot | Wait for reset worker |
| --- | ---: | ---: | ---: | ---: |
| 0 ms | 5.089 ms | 10.467 ms | 5.237 ms | 5.248 ms |
| 5 ms | 10.464 ms | 10.880 ms | 0.276 ms | 0.311 ms |
| 20 ms | 25.150 ms | 9.977 ms | 0.0008 ms | 0.0143 ms |

These pooled sample medians are descriptive, not independent replicates or additive suite-wall components. Background reset queue high-water was four in every case, with reset concurrency at most four. Direct and awaited modes already overlap test/reset activity across different workers; positive overlap alone does not prove improvement. The matched complete-suite contrast supplies that evidence. Timeline-derived occupancy includes async waiting and must not be interpreted as CPU utilization.

## Correctness and failure boundary

All **45 measured cases / 45,000 tests** passed, including final baseline audits and full drain. Each slot first qualified three committed multi-connection cycles, exact baseline/sequence restoration, distinct physical sessions and FK/PK/NOT NULL rejection. Two nine-cell pipeline smokes passed another 216 ordinary tests. Both intentional failure probes threw after committed TRUNCATE but before seeding; the exception surfaced, the identified failed slot stayed Failed with zero Parents rows, no failed slot was published ready, queued/active reset work reached zero, and all reset workers stopped before resource removal. The fault slot was tagged before reset began in the final smoke so premature publication could be detected.

This is a fixed owned schema, with no application use during a slot's reset. User triggers, CHECK constraints, arbitrary schemas, cascading into unowned tables, DDL recovery, cancellation races and failed-slot replacement are not qualified. No feature disabling ordinary constraints or triggers was introduced. A failed batch aborts and disposes its resources; this is not a production recovery implementation.

## Environment and uncertainty

Native Linux Docker via `/var/run/docker.sock`, PostgreSQL 16.15, image `postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`, image ID `sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542`. All cases use ordinary logged tables on an anonymous data volume, bridge networking, 1 GiB memory and explicit zero swap. CPU quota is unlimited in all modes; this is equal quota policy, not a fixed total CPU budget. The fixture and package lock are retained. SDK 10.0.301 built the net10.0 harness; exact runtime configuration and `dotnet --info` are archived.

Only `fsync`, `synchronous_commit` and `full_page_writes` are disabled. `session_replication_role=origin`, read committed isolation, ordinary constraints, JIT and autovacuum remain enabled. Primary runs load no diagnostic extension and have I/O/WAL timers off. Every measured configuration, setting and cached SQL matches across cases. No durability guarantee is claimed for disposable data.

Cgroup memory, CPU and I/O counters span preparation through program disposal, not just the timed suite. Swap, memory-event and CPU-throttling increments are zero. Twelve cases have small memory-pressure increments, at most 249 microseconds; overlapping `some` and `full` totals must not be added. Host one-minute load before cases ranges from 6.34 to 23.74 on 24 logical CPUs. Background host activity was uncontrolled. No native PostgreSQL, WSL2, Windows, other hardware or real application-suite comparison was performed.

## Preserved storage preparation and related reading

The original accepted storage investigation prepared disk/tmpfs × logged/unlogged contenders. After the user clarified ready pooling as the intended direction, storage profiling and the 40-case performance matrix were left **unexecuted**. The prepared source remains at `f1ebc97d0ba3a4d81cf157b4a22c6483821da1e9` and in the parent harness. Its eight-cell final smoke passed 96 ordinary cycles; the prior attempt passed 24 before a setup failure. No storage performance result is claimed.

Two setup failures remain visible: an absent optional Docker `Tmpfs` key, then a tmpfs mount representation assumption. The driver now checks `HostConfig.Tmpfs` and the running filesystem. All failed-attempt resources were removed. Three original storage source snapshots and all used runtime bytes are retained. The later diagnostic code was built and syntax checked but never executed, including its statistics-flush handling. Its [README](../../../../../experiments/tt036/README.md) documents PostgreSQL accounting limitations and primary references.

The user's old Microsoft Instant File Initialization URL could not be loaded. The [current official documentation](https://learn.microsoft.com/en-us/sql/relational-databases/databases/database-instant-file-initialization?view=sql-server-ver16) describes skipping zero-fill for newly allocated data-file space during creation, growth and restore. That does not establish the cause of the reported 3.5-second SQL Server snapshot revert, nor a PostgreSQL equivalent switch. No SQL Server experiment was started here.

## Evidence, reproduction and recovery

Measured checkpoint **`a42246dc2927562eb34dba43f6b2b278d5e948d1`** is preserved in normal merge history. Every primary source hash matches that commit. All 24 runtime DLL/JSON hashes were checked before and after each case and the original executable bytes remain retained. Builds preceded checkpoint creation, so embedded assembly Git metadata may name the preceding commit; the source manifests and retained bytes identify what actually ran. Later documentation does not alter measured code. Early smoke manifests name a preceding HEAD while sources were uncommitted: their exact snapshots, rather than HEAD alone, bind those inputs.

[summary.json](summary.json) contains all round times, comparisons, occupancy and supporting metrics. [validation.json](validation.json) indexes all attempts, source provenance, archive/member hashes and counts. [raw-evidence.tar.gz](raw-evidence.tar.gz) retains raw reports, 45,000 test timelines, receipts, logs, source snapshots, all five runtime versions and storage observations. Extract its contents into `artifacts/tt036/` in a checkout containing the measured commit, then run:

```sh
python3 experiments/tt036/pipeline/analyze.py artifacts/tt036/pipeline-measure
```

This validates frozen source against Git and retained runtime bytes, and reproduces the summary without starting a database. To rerun measurements, follow the harness README and use fresh output/runtime paths; never overwrite an attempted runtime version.

All **77 owned containers and 72 owned volumes** across storage preparation, pipeline smokes and measurement are absent; initial unrelated containers and all starting volume names remain. Every attempt's before/after inventory matches. [resource-inventory.json](resource-inventory.json) binds the exact owned identities and final inventory. No shared caches or unrelated resources were removed. Raw retained evidence is about 67.5 MiB before compression, within the 1 GiB budget. The storage tool's root file scan completed, but category/worktree discovery was unavailable because installed Git rejects `worktree list -z`; a plain worktree inventory independently confirmed the single primary checkout. That limitation is retained, not reported as a clean categorized scan.

All six Release builds succeeded with zero warnings/errors. Python syntax, manifest/runtime/report hashes, statistics and ownership-timeline checks passed. Independent read-only recomputation confirmed all primary intervals and resource limits. This isolated spike makes no local maintained-suite coverage or mutation claim; hosted checks and independent exact-candidate completion review gate publication, with outcomes owned by the linked GitHub issue/PR.

## Next decision

The practical change to pursue is allowing a test to return its database and continue while the pool owns its reset. A production slice needs a deliberate ownership, failure, cancellation and shutdown contract, followed by a representative real-suite benchmark. It is not implemented by this ticket.

Dynamic balancing is a useful later direction: observe lease waits, reset queue length and host pressure, then adjust test workers, reset workers and spare clean capacity within one budget. More infrastructure is useful only while it improves overall suite throughput. This screen fixes capacities and does not prove a controller policy. The important lesson from the scope correction is to evaluate the test scheduler's complete critical path before assuming that individual reset latency is the primary goal. No new standing guidance or memory was written.

Owner understanding remains unconfirmed until discussed; that does not reopen completed experiment delivery.

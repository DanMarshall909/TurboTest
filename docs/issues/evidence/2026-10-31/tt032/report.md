# TT-032 — competitive PostgreSQL baseline

[Authoritative issue #25](https://github.com/DanMarshall909/TurboTest/issues/25). Measurements: 2026-10-31 UTC / 2026-11-01 Brisbane. This is discovery evidence, not a claim that TurboTest has achieved the product's 2× goal.

The actionable result is the **warm, relaxed, 1,000-test data-only workflow**: Respawn plus exact seed/sequence restoration has a **6.467 s median**, against TurboTest's **27.224 s**. The half-time target is **3.233 s**, requiring an **88.1% reduction** from the measured TurboTest median. Rapid acquisition alone does not deliver rapid complete feedback.

The primary matrix attempted 240 independent cases and recorded 45,453 test cycles: TurboTest, Respawn and native templates each completed 60/60 cases; IntegreSQL at the tuned 25 ms lifetime completed 51/60. Every attempt has verified owned-resource cleanup. A separately qualified 250 ms IntegreSQL lifetime configuration and its paired references completed another 60/60 cases, recording 2,420 cycles. Its larger workflows did not beat the paired reference. **A unique combined leader for short fresh workflows remains unresolved.** Phase-specific medians and uncertainty are retained below; results from different phases are not pooled.

## Claim and common contract

The claim concerns native Linux x64, .NET 10 and PostgreSQL 16. Dan accepted Linux/WSL2 as the platform boundary; these native Linux measurements do not establish WSL2 or native Windows performance. The workload is a small SQL-owned relational schema used through an EF Core context, not EF's migration runner or a large-dataset benchmark.

Three related tables contain 32 parents, 64 children and 128 notes, plus an immutable version table. A version-1 SQL migration is idempotent. Every test commits 100/20/10 inserts plus seed updates/deletes through two simultaneously open physical connections. A third connection observes the committed result. Independently constructed expected values check rows, relationships, markers and sequence state; baseline sequences are restored to 1000/2000/3000. The version table is checked but excluded from Respawn's data reset, like immutable migration metadata. The declared application workload does not mutate it.

The primary leaderboard covers data changes to those three tables with a fixed schema. A separate column-addition probe checks replacement and stale connections. It does not establish equal automatic detection of arbitrary DDL, session settings, temporary objects, role changes, privileged writes or connections the caller refuses to close. Workload logins own their databases but are NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOREPLICATION and NOBYPASSRLS. Privileged administration is separately owned. All test connections belong to their lease; the stale-reference probe checks both the disposed context and the actual closed connection, old backend PID and old database OID.

| Actual contender | Ordinary restoration included in timing | Schema-replacement boundary | Failure boundary |
| --- | --- | --- | --- |
| Respawn 7.0.0 + explicit SQL seed/sequence restore | Cached Respawn graph, PostgreSQL TRUNCATE/CASCADE, full reseed and exact setval | Caller explicitly invalidates schema; wrapper replaces from the native template | Failed reset leaves the slot unavailable; fixture cleanup and fresh recovery are checked |
| Native PostgreSQL templates | Pooled application connections, bounded asynchronous drop/CREATE DATABASE TEMPLATE with WAL_LOG | Every return replaces the database | SQL errors propagate; one 15 s DROP timeout occurred in qualification |
| IntegreSQL v1.1.0 | Actual API/controller, automatic recreation, bounded pool, a physical connection pinned for each lease | Every issued database is recreated | No synchronous failed-reset receipt; caller deadline, controller evidence and fixture replacement are required |
| TurboTest public API | Public factory/lease/context, current row restoration and validation, native fallback | Current API detects the tested schema change and replaces the database | Public lifecycle error, quarantine/cleanup and fresh recovery are checked |

Fresh construction runs migration and seed callbacks once per fixture. Cached construction means reuse within that fixture, not a persistent cache across processes. Reapplication of the SQL migration is tested explicitly; repeated and reordered test cycles do not rebuild the baseline. Respawn's per-return reseeding SQL remains inside its restoration time even though the initial construction callback is cached.

## Timing and fairness

The [frozen protocol and commands](../../../../../experiments/tt032/README.md) precede representative timing. One test, 20 affected tests and sustained suites run sequentially or with four workers, from both fresh and ready fixtures. The durable sustained suite has 100 tests; the relaxed suite has 1,000. Durable template-deletion pilots made repeated 1,000-test durable runs impractical. The 100-test result does not establish 1,000-test durable behavior.

Fresh ranking uses supervisor **process launch through exit**, including preparation, assertions, final restored-capacity audit, report writing and drain. Warm ranking uses **SuiteMs**, from first request through all returns, replenishment and the exact restored-capacity audit. Warmup and final service destruction remain separately recorded. Component sums such as CompleteMs are not external process wall time. IdentitySqlMs covers connection/identity-query work; FirstUsefulSqlMs runs from request to the first baseline-query result. These intervals overlap and must not be added.

Each scenario has five independent process runs. A seeded schedule rotates contender positions; each position occurs once in the first four rounds and at most twice across five. Two matched pilot rounds compare one versus four restoration workers per contender/configuration. Four is selected only when both pass and its median warm suite is at least 5% faster. This selected four for every parallel case; sequential cases use one. IntegreSQL's pilot variance remains visible in [tuning-summary.json](tuning-summary.json); the rule does not prove universal optimality.

Every PostgreSQL container has a 256 MiB limit. IntegreSQL's controller has a separate 64 MiB allowance. Capacity is one or four workload databases, at most four workload connections per database, four preparation/administration connections plus one observer. IntegreSQL does not impose a hard Go database/sql connection ceiling: its bounded workers and held-workload observations qualify this local usage, not an intrinsic connection guarantee. The held-resource probe checks 12 simultaneous workload connections, distinct databases, per-database/service counts and a combined ceiling of 21. The pinned IntegreSQL connection is included in its four-per-database allowance.

All cases use the same host CPU access and persistent Docker volumes. The machine is an AMD Ryzen 9 5900X (12 cores/24 threads), about 31.3 GiB RAM, Pop!_OS 22.04 x64, with ext4 on a CT2000P3PSSD8 NVMe device. Docker is 29.7.2/API 1.55; SDK 10.0.301 and runtime 10.0.9 are recorded. There is no CPU affinity or exclusive-host claim: unrelated work remained running, and before/after load is recorded for each case. Image/runtime caches are warm; downloads and SDK installation are outside timing. No RAM disk or verified block-sharing optimization is used or credited.

All adapters use `--no-sync` during initdb. “Durable” names the running PostgreSQL settings, not a crash-durability test. Durable keeps fsync, synchronous_commit and full_page_writes on. Relaxed turns all three off **after identical durable preparation**, because the current TurboTest API lacks equivalent launch flags; that transition is counted in preparation. Thus this is not an all-relaxed-startup optimum. Boundary snapshots record fsync and synchronous_commit; separate diagnostic observations confirm all three settings and the workload/admin role separation. Common application SQL uses a 15 s deadline; IntegreSQL HTTP/settlement uses 60 s, the run deadline is 20 minutes, and the supervisor deadline is 1,500 s. Native DROP also uses 15 s. Tool-internal defaults are not represented as identical error APIs.

## Primary matrix

Values are milliseconds. Each cell is a median of five successful complete cases unless marked with its successful count. Marked cells are **conditional on success** and are not eligible for an unconditional complete ranking. The [full summary](summary.json) and [timing detail](timings.md) include raw run values, p95/max, throughput, operation tails, components and uncertainty.

| Durability · tests / workers · mode | Respawn | Native templates | IntegreSQL25 | TurboTest |
| --- | ---: | ---: | ---: | ---: |
| durable · 1 / 1 · fresh | 4,034.8 | 5,340.5 | 10,367.5 | 8,761.3 |
| durable · 1 / 1 · warm | 89.5 | 174.8 | 213.0 | 138.6 |
| durable · 20 / 1 · fresh | 4,587.2 | 6,447.3 | 11,698.4 (4/5) | 8,408.0 |
| durable · 20 / 1 · warm | 1,100.9 | 6,421.1 | 3,227.3 | 2,475.3 |
| durable · 100 / 4 · fresh | 10,116.4 | 14,841.4 | 38,664.5 (2/5) | 11,523.9 |
| durable · 100 / 4 · warm | 2,008.3 | 8,743.1 | 31,701.5 (4/5) | 2,888.4 |
| relaxed · 1 / 1 · fresh | 4,538.1 | 4,034.9 | 10,068.5 | 7,703.9 |
| relaxed · 1 / 1 · warm | 63.4 | 114.4 | 132.6 | 135.3 |
| relaxed · 20 / 1 · fresh | 4,989.2 | 6,246.0 | 16,398.3 | 8,156.3 |
| relaxed · 20 / 1 · warm | 444.3 | 2,620.4 | 10,839.3 (4/5) | 2,022.8 |
| relaxed · 1000 / 4 · fresh | 9,763.5 | 46,301.3 | 221,730.5 (3/5) | 33,584.4 |
| relaxed · 1000 / 4 · warm | 6,466.7 | 42,596.2 | 162,125.1 (4/5) | 27,224.1 |

The observed reference below is the smallest median among contenders with five successful representative cases in that scenario. Native's earlier qualification timeout remains a separate caveat; a diagnostic pass does not erase it. IntegreSQL's failed cells cannot be called dependable winners. None of the TurboTest medians meets the half-time target.

| Scenario | Primary observed reference | Reference median [95% interval] ms | Half-time target ms | TurboTest reduction required |
| --- | --- | ---: | ---: | ---: |
| durable · 1 / 1 · fresh | Respawn | 4,034.8 [2,677.7, 5,442.1] | 2,017.4 | 77.0% |
| durable · 1 / 1 · warm | Respawn | 89.5 [86.0, 126.2] | 44.7 | 67.7% |
| durable · 20 / 1 · fresh | Respawn | 4,587.2 [3,682.3, 7,401.6] | 2,293.6 | 72.7% |
| durable · 20 / 1 · warm | Respawn | 1,100.9 [948.1, 3,646.5] | 550.5 | 77.8% |
| durable · 100 / 4 · fresh | Respawn | 10,116.4 [6,396.5, 20,872.9] | 5,058.2 | 56.1% |
| durable · 100 / 4 · warm | Respawn | 2,008.3 [1,823.5, 7,010.5] | 1,004.2 | 65.2% |
| relaxed · 1 / 1 · fresh | Native templates | 4,034.9 [2,978.7, 6,246.7] | 2,017.4 | 73.8% |
| relaxed · 1 / 1 · warm | Respawn | 63.4 [52.6, 80.2] | 31.7 | 76.6% |
| relaxed · 20 / 1 · fresh | Respawn | 4,989.2 [4,034.3, 5,642.5] | 2,494.6 | 69.4% |
| relaxed · 20 / 1 · warm | Respawn | 444.3 [389.4, 602.2] | 222.2 | 89.0% |
| relaxed · 1000 / 4 · fresh | Respawn | 9,763.5 [9,663.6, 11,321.3] | 4,881.7 | 85.5% |
| relaxed · 1000 / 4 · warm | Respawn | 6,466.7 [6,290.6, 6,802.7] | 3,233.4 | 88.1% |

Intervals are exact empirical bootstrap intervals for the median across the available successful independent runs (n ≤ 5), not a hardware-population or familywise guarantee. At these sample sizes, a nearest-rank suite p95 is the largest available successful run; it is not a stable population-tail estimate. Per-operation tails pool descriptive samples and do not create thousands of independent suite replicates. Overlapping short-fresh intervals do not establish a unique statistical winner.

## Separate 250 ms lifetime sensitivity

The 25 ms configuration produced first-connection failures, so the predeclared addendum checks the documented **250 ms lifetime** while retaining the other tuned settings. This is not an all-default configuration. Full qualification and a durable 100-test stress case pass before the paired phase. Five pairs per selected scenario alternate order, with a contemporaneous reference; all 60 attempts pass. [The separate summary](lifetime250-summary.json) retains paired raw values and paired-bootstrap uncertainty.

For successful automatic recreation at fixed capacity P, settled time is at least `ceil(N/P) × 250 ms`: each slot must pass its lifetime before it can be recreated and issued again, including the final replenishment. The bound does not apply to manual recreation. A scenario is measured if the original reference's upper median interval reaches that floor; otherwise only an analytic exclusion is recorded. The excluded scenarios have no newly measured 250 ms dependability result. For the 1,000-test/four-worker case, the 62.5 s floor cannot beat the retained Respawn reference evidence.

| Scenario | IntegreSQL250 median ms | Paired reference median ms | Candidate/reference ratio [paired 95% interval] | Pair half-time target ms |
| --- | ---: | ---: | ---: | ---: |
| durable · 1 / 1 · fresh | 4,084.8 | Respawn 3,682.3 | 1.11 [0.85, 2.05] | 1,841.2 |
| durable · 20 / 1 · fresh | 12,530.1 | Respawn 5,491.7 | 2.28 [1.46, 3.45] | 2,745.8 |
| durable · 100 / 4 · fresh | 35,498.8 | Respawn 4,890.0 | 7.26 [3.83, 10.03] | 2,445.0 |
| durable · 100 / 4 · warm | 13,729.0 | Respawn 1,924.7 | 7.13 [6.54, 15.81] | 962.3 |
| relaxed · 1 / 1 · fresh | 4,336.4 | Native templates 4,789.4 | 0.91 [0.62, 1.97] | 2,168.2 |
| relaxed · 20 / 1 · fresh | 14,741.4 | Respawn 3,230.6 | 4.56 [2.97, 4.95] | 1,615.3 |

The larger measured scenarios favor the paired reference. Both fresh one-test ratio intervals include 1, so their combined leader is unresolved. A pair target is half the smaller median in that pair's phase; it does not replace the primary target or establish superiority over competitors absent from that phase. Their stricter minimum may guide provisional engineering planning, but is not a validated combined fastest-competitor threshold. Passing these 250 ms cases does not prove repair of either observed lifecycle race.

## Dependability and retained failures

Qualification covers fresh/cached construction, idempotent migration, concurrent isolation, reversed/repeated order, committed writes followed by test failure, canceled admission, canceled SQL, a column addition, stale physical connections, reset failure, exact cleanup and a fresh recovery fixture. Deliberate seed, sequence and workload corruption must produce the named SeedState, SequenceState and NoteWork diagnostics. Held workloads verify the resource allowance. Server-loss and failed-drain recovery are exercised; the Docker daemon itself is not deliberately failed. False volume-absence evidence is rejected by the supervisor fault controls.

The primary matrix retains nine IntegreSQL25 failures: two HTTP acquisition timeouts (indices 021 and 198), four database-not-found errors while opening the pin (076, 162, 186, 230), and three capacity-settlement timeouts after all 1,000 tests returned (143, 158, 178). Case 143 independently shows one slot's observed generation 212 against required 213. Counting only returned tests would have hidden that failed workflow. These are observed counts across different scenarios, not an estimated IID failure probability.

Pinned upstream source admits a replenishment interleaving: ready publication is followed by unlock before the recreation counter is decremented; an acquisition can see that counter and omit scheduling the next cleanup. Its task forwarder can also drop work when full. Debug logs do not prove which path caused any timeout. The database-not-found errors are consistent with a 25 ms lifetime expiring before the first connection opens, but the later successful setting is not causal proof. No custom controller patch, manual-recreate workaround or timeout increase was used to turn a failed case into a pass.

Native templates had one ordinary DROP timeout during final qualification. Exact cleanup passed. A separately instrumented run with the same binary, configuration and 15 s deadline passed all probes and observed checkpoint waits, including a 4.965 s forced checkpoint with 4.383 s synchronization. No server diagnostics were captured for the original failure, so its cause remains unproven. Both attempts are retained.

Earlier development failures are also retained: an IntegreSQL template-ownership setup error, a qualification OID parameter type error, and an overly narrow expected-error classifier after a deliberately stopped TurboTest server. These were harness/adapter repairs before final evidence, not hidden product successes. An initial tuning supervisor preflight also failed before any case ran. Historical development attempts are not pooled into performance statistics.

The original representative supervisor stopped after case 021 because it required a final image snapshot even for a failed run. Source/binary and initial image evidence still matched. A hash-bound continuation preserved all 22 receipts and ran only indices 022–239; no attempt was retried. The final supervisor now validates every available image observation, requires both boundaries for success, and stops on missing or failed container/volume cleanup proof. Fault controls reject incorrect images, missing success snapshots, failed volume absence and inventory drift. The existing maintained benchmark tickets TT-013/014/016 remain separate; these are isolated discovery controls.

## Resources and cost evidence

The following maxima/ranges cover successful primary attempts, including successful cases in incomplete cells. Failed-attempt samples remain in the raw archive. PostgreSQL usage is the Docker-reported container usage metric; client memory is process working set. Each peak is a separate sampled observation, not a simultaneous aggregate. Monitoring starts after preparation, so startup peaks are not established.

| Contender | Client working-set peak MiB | PostgreSQL usage peak MiB | Controller usage peak MiB | Maximum final PG allocation MiB | PG allocation growth range MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Respawn | 146.4 | 256.0 | 0.0 | 130.6 | 0.0–64.8 |
| Native templates | 153.7 | 205.0 | 0.0 | 103.9 | 21.4–38.8 |
| IntegreSQL25 | 160.6 | 215.1 | 14.1 | 103.5 | 11.4–38.5 |
| TurboTest | 159.0 | 255.9 | 0.0 | 166.5 | 0.1–93.5 |

Every successful case returns to the declared workload-database count and exact baseline; the final audit checks all 240 cleanup receipts and global inventories, including the last receipt after the external cleanup watcher stopped. Disk growth includes normal PostgreSQL allocation/WAL effects; finite runs and sampled memory do not establish indefinite leak freedom. All task-owned containers and anonymous volumes are absent after every attempt; unrelated resources are preserved.

Separate 100-test/four-worker TurboTest diagnostic profiles exclude four warmup returns. Combined validation accounts for **67.2% durable / 70.1% relaxed** of aggregate reset execution; median reset execution is **71.0 / 68.7 ms**, and median combined validation is **48.0 / 48.0 ms**. These are diagnostic component observations, not ranking runs or independent performance replicates. The settings observer may overlap those diagnostic runs. [Profile totals](profile-summary.json) and raw events are retained.

The source shows concrete costs: clearing workload connections; an unpooled administrative connection; schema/support capture; separate deletes, seed imports and sequence operations; a second schema/content capture; and workload-connection verification. Validation combines catalog queries, server serialization/sorting/hashing, transfer, client hashing and verification. It does **not** establish hashing alone as dominant. Concurrent component totals cannot simply be subtracted from suite wall time, and removing current validation wholesale would weaken TurboTest's broader automatic schema contract.

## One bounded next experiment

Prototype **one compiled data-restoration SQL command on a reused bounded connection**, in this isolated comparison harness. Keep the fixed-schema/data-only boundary, explicit schema invalidation and native replacement. Compile dependency-ordered deletes, deterministic seed restoration and exact sequence restoration into one transactional command; count plan preparation and any seed storage. Keep the same application SQL, independent assertions, connection/resource limits, cancellation probes, stale-connection checks and settled-capacity audit. A failed reset must quarantine the slot and replace it before reuse; sequence effects require recovery even when a transaction fails.

The primary success criterion is the relaxed warm 1,000-test/four-worker scenario: all qualification and cleanup checks pass, and the upper 95% bound for candidate/reference complete elapsed-time ratio is at most **0.5** across at least five new paired independent runs. The current planning target is **3.233 s**. Report other workflows separately and retain losses. This would establish only that scenario, not the product-wide 2× claim.

Stop or pivot if the unchanged application/assertion/settlement work leaves insufficient headroom, the bounded prototype cannot beat the reference, or improvement requires weaker ownership/checks or moving work outside accounting. Adopting Respawn remains a rational simplification if custom restoration cannot justify itself. No production backend change or next implementation goal is started by this report. Profiling identifies a plausible experiment; it does not prove the target is reachable.

## Reproduction, provenance and acceptance

Measured experiment commit: `f077440782fd742144c89b86577c2d798e18e4ad`. TurboTest production source is unchanged from `221493ba28fa0f2d152563766dcafd3652796994`. Tuning used `e09083a`; the later analysis/reporting corrections do not change the timed C# workload or adapters. Normal merge history preserves these checkpoints. Exact SDK, package graph, source/binary hashes, images, filesystem/storage, settings, plan and per-attempt receipts are retained.

PostgreSQL image: `postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea` (16.15). Controller image: `ghcr.io/allaboutapps/integresql@sha256:66b7433399f1907bad9e4b7cc7bd528ed4ebfccddecb8fd65da4af4d06a68c5a`. IntegreSQL source: `9e395cd7cbc7a0edf40c8d6348f0c0d215c6f7a0`; Respawn source: `f87da03f2a254707b2fca4c22371e89da3ddd241`. The [package lock](../../../../../experiments/tt032/packages.lock.json) records the dependency graph. pgtestdb v0.1.1 (`29c5e550b7a3714e11c5c1081cc338776068cbfd`) is prior art for the competent native control, not a fifth measured tool. Transaction rollback cannot satisfy this committed multi-connection workload. No other contender had evidence warranting expansion of this bounded PostgreSQL comparison.

[Raw evidence archive](raw-results.tar.gz) and [SHA-256 inventory](evidence-manifest.json) contain every development/final attempt used here, source patches, qualifications, diagnostics, plans, receipts, controller logs, calibration and exact continuation/sensitivity drivers. Archive paths are relative to `artifacts/tt032`; extract into a new directory and verify the inventory before inspection. The archived source-bound selection summary remains separate from later presentation. Private handoffs, drafts and unrelated process command lines are excluded; downloaded vendor source/caches are retained locally and not duplicated in the archive.

| Criterion | Evidence |
| --- | --- |
| AC1 | Frozen protocol, platform decision, scenario-specific complete-time metrics and targets |
| AC2 | Common workload, restoration matrix, independent corruption/replacement probes and explicit exclusions |
| AC3 | Pinned tools/images, matched worker tuning, resource/settings records and separate lifetime sensitivity |
| AC4 | Fresh process wall, warm settled suite, all component boundaries and final drain in raw reports |
| AC5 | 240-case fixed plan, five independent rounds, complete receipt/hash audit, descriptive tails/uncertainty/resources |
| AC6 | Final qualifications, retained native timeout, nine primary IntegreSQL failures, 250 ms qualification/stress/pairs, exact cleanup |
| AC7 | Phase-specific leaders/targets, unresolved short-fresh exception, diagnostic cost evidence and one bounded next experiment |

Repository validation is indexed in [validation.json](validation.json). Independent completion review and publication are recorded on the pull request linked from the authoritative issue. This evidence does not close the separate maintained-benchmark or product robustness tickets, and no production behavior changed.

Primary upstream contracts: [IntegreSQL v1.1.0](https://github.com/allaboutapps/integresql/tree/v1.1.0), [pool lifecycle implementation](https://github.com/allaboutapps/integresql/blob/v1.1.0/pkg/pool/pool.go), [Respawn v7.0.0](https://github.com/jbogard/Respawn/tree/v7.0.0), [pgtestdb v0.1.1](https://github.com/peterldowns/pgtestdb/tree/v0.1.1), and [PostgreSQL 16 CREATE DATABASE](https://www.postgresql.org/docs/16/sql-createdatabase.html). These sources establish implementation/configuration contracts; all performance numbers above come from the retained local measurements.

# Cached TRUNCATE did not demonstrate a performance gain

Investigation for [TT-035](https://github.com/DanMarshall909/TurboTest/issues/31). The combined cached reset is correct for the qualified fixed fixture, but the five paired runs do not justify production adoption or the original 2× performance target. Defer adoption and stop this experiment; do not select extra rounds based on the observed result.

| Complete 1,000-cycle suite, four workers | Median |
| --- | ---: |
| Actual Respawn 7.0.0 plus seed/sequence restoration | 5,237.46 ms |
| Cached TRUNCATE plus seed/sequence restoration | 4,915.35 ms |

Candidate/reference ratio: **0.9385**, a 6.15% lower point estimate. Paired bootstrap 95% interval: **[0.5166, 1.3990]**. It spans roughly 48% lower time through 40% higher time, including no difference. The candidate wins three pairs and loses two. This is no demonstrated gain, not evidence of equivalence. The interval does not support 2×; five pairs on one shared host do not establish a universal speed limit either.

| Pair, execution order | Respawn ms | Cached ms | Cached/Respawn |
| --- | ---: | ---: | ---: |
| 0, Respawn first | 5,237.46 | 6,105.01 | 1.1656 |
| 1, cached first | 9,612.88 | 4,965.99 | 0.5166 |
| 2, Respawn first | 3,513.55 | 4,915.35 | 1.3990 |
| 3, cached first | 6,453.72 | 3,355.20 | 0.5199 |
| 4, Respawn first | 5,067.04 | 3,709.56 | 0.7321 |

All ten measured cases and **10,000 measured cycles** succeeded, with no excluded failures or replacement rounds. Ordinary application assertions remain inside every timed cycle. Qualification and one final batch audit establish exact seed and sequence restoration outside timing; there is no per-reset baseline scan or hash.

## What was compared

The [harness](../../../../../experiments/tt035/README.md) reuses TT-032's three-table, 224-row seed and committed multi-connection workload. Each of four worker databases has three pooled application connections and one held admin connection. Each worker executes 250 complete cycles, waiting for reset before continuing. Suite wall time includes all four workers finishing their final reset. Setup, qualification, five warmup cycles per worker, final audit and container disposal are excluded and separately recorded.

Both arms call Respawn's table discovery once, excluding `SchemaVersion`. Both use the same emitted SQL:

```sql
truncate table "public"."Notes","public"."Children","public"."Parents" cascade;
```

Reference: actual `Respawner.ResetAsync(admin)`, then the existing seed/setval command. Candidate: one cached Npgsql command containing `BEGIN`, the exact same truncate, the exact same seed/setval SQL, and `COMMIT`. The full SQL is retained per worker in every report. This changes both command packaging and transaction scope; the measurements cannot attribute an effect to batching alone. One command is not a measured network roundtrip count. Neither arm rediscovers the schema per reset. Respawn's `WithReseed` remains false; both explicitly restore sequences in `SeedSql`.

The pinned [Respawn source](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/Respawner.cs) exposes cached `DeleteSql` and performs its own reset transaction. The candidate therefore builds on the same bulk-reset mechanism, rather than discovering a new PostgreSQL optimization. The earlier [source study](../tt034/respawn-study.md) supplies additional context.

Descriptive medians over the recorded cycles were 10.63 ms reset / 5.42 ms application for Respawn and 8.81 ms / 4.72 ms for cached. These pooled cycle figures are not independent experiment replicates, do not sum to the suite wall clock under concurrency, and do not establish a statistically reliable reset-only gain. The primary sampling unit is the paired suite round.

## Contract and limits

Fixed schema, owned table set, known seed and sequence state, and no active application use during reset are required. Three qualification cycles per worker check exact baseline rows and sequence state, independent physical connections, committed visibility and generated IDs; FK rejection is also qualified. The fixture uses identity keys, primary keys, NOT NULL and foreign keys. User triggers, every constraint type, isolation anomalies, arbitrary schemas, cyclic foreign keys, concurrent clients, DDL recovery and failed-slot reuse are not qualified. PostgreSQL TRUNCATE can cascade beyond a selected table list, so this is not a containment guarantee for arbitrary schemas.

Nothing changed in production code, maintained tests, public APIs or CI. This is a small SQL experiment, not a replacement TurboTest backend. Earlier TT-032/033/034 absolute timings have different harness/resource boundaries and are not interchangeable baselines.

## Environment and uncertainty

Native Linux Docker, PostgreSQL 16.15, image `postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`, local image ID `sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542`. Same bridge networking, 1 GiB memory, explicit zero swap allowance, and no CPU quota for both arms. Only `fsync`, `synchronous_commit` and `full_page_writes` are disabled; `wal_level=replica`, JIT/autovacuum enabled, `session_replication_role=origin`, isolation read committed. The database is disposable; no durability guarantee is claimed. All observed configuration objects and cached SQL match across the ten cases. Runtime/SDK/package lock, settings and host observations are archived.

The maximum observed container-lifetime memory peak is 280.66 MiB. All measured-case memory-event counters and swap current/peak are zero. One Respawn case (pair 3) records 17 microseconds of pressure in each of the overlapping `some` and `full` totals; do not add them. All other pressure totals are zero. Peaks are lifetime high-water marks, not measured-interval peaks, and counter observations span program setup through disposal. Host one-minute load observations vary from 6.94 to 15.29 on 24 logical CPUs; background host activity is uncontrolled and its causal contribution is not measured. No native PostgreSQL control or WSL2 measurement was run.

The preregistered comparison uses a deterministic paired bootstrap: 20,000 resamples, seed 35035, ratio of medians, nearest-rank 2.5/97.5 percentiles. Both members of each round are sampled together. Five pairs provide limited uncertainty resolution. The later correction of the lower quantile index by one order statistic did not change either endpoint for this dataset; original and corrected analyses are retained. No result-dependent extension was made.

## Evidence and recovery

The measured source checkpoint **`f444f3b5de2515bbfe710d72c8d50597ffe25cb5`** is preserved in history. Every measured source hash matches that checkpoint; runtime DLL/JSON hashes were checked before and after every case. Later changes only ensure disposal errors retain a failed report and remaining disposals, prevent log-capture errors from skipping container cleanup, correct the percentile convention, and document results. Disposal and log handling occur after the timed suite. The final harness has a fresh two-arm smoke; an injected Docker-log timeout also verifies owned cleanup still runs. No injected disposal-error execution is claimed.

The initial two-arm smoke and final two-arm smoke both passed their 12 measured smoke cycles each (48 total). Initial smoke source bytes were reconstructed and verified against captured hashes. Final smoke source hashes identify post-checkpoint edits even though its manifest's HEAD still names the earlier checkpoint. The original performance executable was overwritten by the later build; its observed hashes and exact source checkpoint remain, but the original executable bytes are not archived. Final smoke executable hashes match retained build output. These distinctions are explicit in [validation.json](validation.json).

[summary.json](summary.json) is reproduced by `python3 experiments/tt035/analyze.py artifacts/tt035/measure`. [raw-evidence.tar.gz](raw-evidence.tar.gz) contains all 105 retained input/report/receipt/log/calibration members; its SHA-256 is `eb6ccccdabc343aeab84ee3cf95cd7f0c6017fa43f18a6ba2bd9e25a30df6d6f`. Per-member hashes and source validation are in [validation.json](validation.json).

All **14 owned containers and 14 owned volumes** from measurement and smokes are absent. The two unrelated starting containers and all starting volume names remain, confirmed by fresh inventory in [resource-inventory.json](resource-inventory.json). The only missing PostgreSQL log is intentional in the timeout calibration; its error and successful cleanup receipt are retained. Builds and evidence are retained within the existing storage budget; no shared cache or unrelated resource was deleted.

Both Release builds succeeded with zero warnings/errors; Python syntax checks, source/hash checks, statistics recomputation and diff checks passed. No local maintained-suite or mutation claim is made for this isolated experiment. Existing hosted CI and independent completion review gate publication; their exact outcomes belong to the linked GitHub task/PR.

Practical consequence: one combined reset command alone is not an evidence-backed route to a 2× improvement on this fixture. Keep Respawn as the reference, defer this candidate, and require a separately agreed experiment before pursuing another mechanism. Owner understanding of this handover remains unconfirmed until discussed; that does not reopen the completed experiment.

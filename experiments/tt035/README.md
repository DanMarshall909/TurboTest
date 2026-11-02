# TT-035 cached TRUNCATE screen

Isolated experiment for [TT-035](https://github.com/DanMarshall909/TurboTest/issues/31), not a production reset backend.

Both contenders discover the table plan once using Respawn 7.0.0 (`f87da03f2a254707b2fca4c22371e89da3ddd241`). Reference calls actual `ResetAsync` then the existing seed command. Candidate caches `BEGIN; <DeleteSql>; <SeedSql>; COMMIT;` and executes one Npgsql command. Both use identical multi-table TRUNCATE CASCADE and seed/sequence SQL. Packaging and transaction scope change together. One command does not assert one network roundtrip.

Contract: fixed schema and owned table set, no concurrent use of a worker during reset, fixed 224-row seed, explicit sequence restoration. Four worker databases share one PostgreSQL server; each has three pooled application connections and one held admin connection. No container pool, queue or background reset. Each worker waits for complete reset before its next cycle. Unknown schema, concurrent clients, arbitrary seed serialization, DDL recovery and failed-slot reuse are outside this spike. Any error aborts the attempt and its owned container is removed.

The fixture contains identity keys, NOT NULL and foreign-key constraints. Three qualification cycles check committed writes via independent physical sessions, exact baseline rows/sequences and FK rejection. Qualification and final batch audit are outside timing. Every measured cycle includes connection leases, mutation, committed observer assertions, connection return and synchronous reset. No per-cycle baseline/schema audit. Normal triggers and integrity settings stay enabled, though this fixture does not exercise user triggers or every constraint type.

Native Linux Docker with pinned PostgreSQL 16 image; bridge networking, 1 GiB memory and explicit zero swap allowance. Only `fsync`, `synchronous_commit`, `full_page_writes` are disabled for disposable data. Defaults otherwise retained and recorded. Fresh container per contender, initial/final resource inventory and per-case cgroup counters retained. Memory peaks are lifetime high-water marks, not timed-interval peaks.

Five paired 1,000-cycle batches (250 per worker), alternating contender order; three qualification and five warmup cycles per worker. Primary result is complete suite wall time. Bootstrap the five paired rounds together (seed 35035, 20,000 resamples), estimating ratio of contender medians with a percentile 95% interval. An upper bound below 0.5 supports the original 2× target; below 1 supports a gain; otherwise no demonstrated gain, not equivalence. Five pairs give limited uncertainty resolution. Prior experiments have different boundaries and are context only. No extra rounds are selected from the observed result.

```sh
dotnet build experiments/tt035/TruncateScreen.csproj -c Release
python3 experiments/tt035/run.py --smoke --output artifacts/tt035/smoke
# Commit the complete measured source checkpoint before the measurement:
python3 experiments/tt035/run.py --output artifacts/tt035/measure
python3 experiments/tt035/analyze.py artifacts/tt035/measure
```

Runner aborts on failure, drift or unproven cleanup, retaining the attempt. No broad Docker cleanup. Retain compiled dependencies and evidence; only exact owned containers and volumes are automatically removed. The source checkpoint and executable hashes bind measurements to code. Reproduction creates new output folders and must preserve previous attempts.

Measured checkpoint: `f444f3b5de2515bbfe710d72c8d50597ffe25cb5`. Later changes preserve disposal failures in the report, attempt all worker disposals, and ensure a failed log capture cannot skip owned container removal. These run after the timed suite. A final two-arm smoke qualifies the revised harness; the original ten measurements stay bound to their original checkpoint/binary. Analysis now uses nearest-rank percentiles consistently; the endpoint correction did not change this dataset's interval. No additional performance rounds were selected.

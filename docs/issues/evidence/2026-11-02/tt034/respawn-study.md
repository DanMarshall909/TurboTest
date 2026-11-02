# Why the measured Respawn path is fast

Dan requested this source study after the E-Maj and Docker controls. This is
diagnosis, not a new implementation or causal profiling experiment.

The installed Respawn 7.0.0 nuspec identifies commit
`f87da03f2a254707b2fca4c22371e89da3ddd241`, also tag v7.0.0. Its PostgreSQL adapter
differs from the README's generic ordered-DELETE description:

1. **Prepare once.** CreateAsync queries tables/FKs, builds their graph and caches
   generated SQL. Discovery is not repeated on each reset.
   [Setup](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/Respawner.cs#L153-L171).
2. **Use native bulk removal.** The default PostgreSQL command contains one
   multi-table TRUNCATE with CASCADE. Our acyclic graph adds no cycle-handling
   trigger-disable/enable statements. No custom formatter was configured.
   [Adapter](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/PostgresDbAdapter.cs#L170-L196).
3. **Keep repeated work small.** ResetAsync begins a transaction, executes cached
   SQL, optionally executes reseeding SQL, and commits. It does no client metadata
   rediscovery, baseline-content scan or hash verification. One bulk reset command
   is not a claim of one network round trip: transaction/commit work remains.
   [Reset](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/Respawner.cs#L109-L125).
4. **No write journal.** Respawn installs no change-log triggers on application
   writes. It accepts an open connection and owns no special pool. TT-034 gives
   both contenders the same held administrator connection and application pool.

WithReseed defaults false and TT-034 leaves it false. The emitted TRUNCATE omits
RESTART IDENTITY. **Our timed harness separately restores all 224 seed rows and
three explicit sequence values**, so that work is included. Respawn's optional
reseed implementation performs catalog-based sequence work on each reset; it is
not the measured configuration.
[Options](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/RespawnerOptions.cs#L6-L15),
[reseed](https://github.com/jbogard/Respawn/blob/f87da03f2a254707b2fca4c22371e89da3ddd241/Respawn/PostgresDbAdapter.cs#L198-L224).

PostgreSQL TRUNCATE removes contents without scanning/deleting individual rows and
reclaims storage immediately. It takes exclusive locks and fires TRUNCATE rather
than DELETE triggers. CASCADE can include referencing tables outside the configured
filters, so ignores are not a containment guarantee. This reset requires ownership
of the relevant table set and no in-flight application work; ordinary test-time
constraints remain enabled. [PostgreSQL semantics](https://www.postgresql.org/docs/16/sql-truncate.html).

## Comparison with the other mechanisms

E-Maj logs every mutation, then plans and executes undo. Version 5.0.1's _rlbk_tbl
creates/drops an internal unlogged key-work table for each changed table and
rebuilds affected rows with deletes/inserts. Broader rollback separately deletes
logs through _delete_log_tbl and manages sequences, triggers and history.
The internal work table is distinct from the
unsupported UNLOGGED application-table case.
[Pinned source, sql/emaj--5.0.1.sql lines 4920–4995](https://api.pgxn.org/dist/e-maj/5.0.1/e-maj-5.0.1.zip),
[rollback mechanics](https://emaj.readthedocs.io/en/latest/rollbackDetails.html).

The maintained TurboTest prepared path clears its slot's connection pool, opens
an unpooled administrator connection, captures/verifies schema, runs per-table
DELETE plus binary seed COPY and sequence commands, captures/verifies the restored
baseline, and verifies access as the workload user. Dependency order and seed bytes
are already prepared once; the differences are repeated validation, connection
work and the restoration strategy.
[TtPreparedPool](../../../../../src/TurboTest.EFCore/Internal/TtPreparedPool.cs),
[TtRowResetPlan](../../../../../src/TurboTest.EFCore/Internal/TtRowResetPlan.cs).

For this small seed, bulk empty-and-reseed plausibly costs less than logging and
undoing writes. The first screen measured E-Maj reset-command medians of 51.7–61.0 ms
against Respawn-plus-reseed at 8.0–8.8 ms. This establishes where the difference
appears, not how much each internal operation causes.

## Consequence for the next candidate

A bounded candidate would reuse a prepared table list, native multi-table TRUNCATE
and cached/batched seed/sequence restoration under an explicit fixed-schema
contract. Compare actual Respawn with matching lifecycle and validation boundaries.
TT-033 cached DELETE SQL and changed transaction boundaries together; its negative
result did not isolate batching or test this exact TRUNCATE candidate.

This study does not prove a 2x gain is available. It identifies the cheaper operation
to match and the contract choice: fixed-schema reset can omit repeated discovery,
while the current automatic-schema guarantees require additional work. No production
fast mode or further experiment was implemented by this study.

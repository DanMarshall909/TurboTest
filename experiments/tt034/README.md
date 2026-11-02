# TT-034: E-Maj and disposable PostgreSQL settings

Bounded feasibility spike; no production backend or public guarantee changes.
Compare E-Maj 5.0.1 undo to actual Respawn 7.0.0 plus seeds and sequences.
The owner permits disabling recovery/durability mechanisms but requires ordinary
integration-test semantics: constraints, application triggers, isolation and
committed visibility. E-Maj logging remains enabled. UNLOGGED tables are excluded:
E-Maj supports them only in audit-only groups, not rollbackable groups.

Reuse TT-032's fixed relational data and committed two-writer/third-observer SQL.
One database and one sequential worker screen mechanisms before considering pool
integration. Application connections are pooled, reset uses a bounded administrator
connection; both contenders use the same connection arrangement. Count open/return,
application writes/assertions and complete synchronous reset in suite wall time.
No per-cycle baseline scan/hash/reseed on E-Maj. Qualify exact seed/sequence state,
multiple mark reuses, foreign-key rejection and three distinct physical connections
outside measurement. Sequence equivalence means the next generated IDs; E-Maj's
RESTART uses (1001,false) where setval used (1000,true). Raw last_value/is_called
identity and session-local currval are not this contract. Setup, warmup and teardown are separate. Application assertions
remain identical. This timing boundary differs from TT-032/033: compare only paired
contemporaneous results, not their old four-worker totals.

Use fresh containers, pinned PG16.15 image, 256 MiB memory, unchanged default CPU
budget, one contender at a time. Five independent pairs of 100 cycles per profile,
alternating contender order and rotating profile order each round. Baseline is the already tested relaxed configuration:
fsync, synchronous_commit, full_page_writes off. Independently screen minimal WAL
(wal_level=minimal, required max_wal_senders=0), JIT off and autovacuum off. Report
checkpoint observations; if none occur, do not claim deferred checkpoints improve
this short screen. Default archiving is already off. Do not change isolation,
application-session replication role, constraints or application triggers to
manufacture speed. E-Maj itself temporarily relaxes triggers/FKs during administrative
undo, then restores them; normal application work remains at origin/read committed.
This workload has no application trigger or isolation anomaly scenario, so those
compatibility claims are not qualified by this spike.
Fresh short-lived databases bound the autovacuum-off trial; that result cannot
establish long-running performance without vacuum. No host filesystem changes.

Record settings, server version, dependencies, input hashes, raw samples, failures,
resource configuration and exact container/volume absence. A failed run is retained
and is not a successful timing sample. Stop on failed cleanup. Installer is downloaded
from PGXN, pinned by SHA256, and kept in ignored artifacts (not vendored). A single
final baseline audit follows each batch outside SuiteMs, never on each timed reset.

Stop/defer if E-Maj is uncompetitive in every profile. Expand only if a profile's
median paired E-Maj/Respawn ratio is <=0.65 and all correctness/cleanup checks pass;
the eventual 2x claim requires a separate 1000-cycle/four-worker phase with upper
95% paired ratio <=0.5. Report ratios with paired bootstrap intervals, including
uncertainty and setup costs. Maximum initial screen: 40 containers/4000 measured
cycles, plus at most six smoke containers (including two retained setup/oracle
failures); no speculative optimization sweep.

Build: `dotnet build experiments/tt034/EmajScreen.csproj -c Release`.
Run: `python3 experiments/tt034/run.py --output artifacts/tt034/screen`.
Use `--smoke` for two 3-cycle containers before freezing measured inputs.
The driver expects the official release zip at `artifacts/tt034/e-maj-5.0.1.zip`.

Sources:
- https://emaj.readthedocs.io/en/latest/install.html
- https://emaj.readthedocs.io/en/latest/rollbackDetails.html
- https://emaj.readthedocs.io/en/latest/limits.html
- https://www.postgresql.org/docs/16/non-durability.html
- https://www.postgresql.org/docs/16/runtime-config-wal.html
- https://www.postgresql.org/docs/16/runtime-config-autovacuum.html
- https://www.postgresql.org/docs/16/jit-decision.html

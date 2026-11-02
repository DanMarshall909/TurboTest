# TT-034: E-Maj and disposable PostgreSQL settings

Investigation: [TT-034](https://github.com/DanMarshall909/TurboTest/issues/29).
GitHub owns live status. This file retains evidence and the bounded decision.

## Result and decision

**Stop this E-Maj candidate for the measured workload; defer production adoption.**
All 40 measured runs (4,000 cycles) passed qualification, application assertions,
final batch audit and exact owned-container/volume cleanup. No configuration met
the prespecified median paired ratio <=0.65 required to expand. No 1,000-cycle,
four-worker phase or product backend was implemented.

Each entry is the median complete time for 100 sequential cycles; five independent
pairs per configuration on one shared native Linux host. Both contenders use the
same configuration in each pair. The existing relaxed profile already disables
fsync, synchronous_commit and full_page_writes.

| Additional setting | Respawn | E-Maj | E-Maj/Respawn | Paired 95% interval for ratio of medians |
| --- | ---: | ---: | ---: | --- |
| None: existing relaxed profile | 1.641 s | 6.612 s | 4.029 | [2.503, 6.605] |
| Minimal WAL, max_wal_senders=0 | 2.185 s | 6.273 s | 2.871 | [2.177, 4.406] |
| JIT off | 2.742 s | 6.661 s | 2.429 | [2.268, 3.666] |
| Autovacuum off | 2.700 s | 6.499 s | 2.407 | [2.337, 5.555] |

E-Maj was slower in every pair. Its median reset-command time was 51.7–61.0 ms
across profiles, versus Respawn plus reseed at 8.0–8.8 ms. Application-write and
assertion medians were 7.2–8.5 ms for E-Maj and 4.0–4.4 ms for Respawn. These
component medians describe observations; they are not additive predictions of
suite medians or a causal decomposition of PostgreSQL internals.

The screen's decision statistic is the **median of paired ratios**, separately
from the displayed ratio of medians. Those values are 3.865, 3.353, 2.692 and 2.413
respectively. [summary.json](summary.json) retains both statistics, their explicitly
labelled paired bootstrap intervals, all raw suite times and configuration effects.
Intervals enumerate all 3,125 paired bootstrap samples. Five pairs on one shared
host do not establish performance across hardware or workloads.

## What disabling features showed

Dan explicitly permits sacrificing crash recovery/durability in disposable test
databases while preserving behavior commonly tested in integration tests.

- The major durability settings were already off. Archiving, data checksums,
  statement logging and duration logging were also off in the observed settings.
- Minimal WAL is compatible with this E-Maj use. It does not eliminate WAL for
  ordinary logged-table DML. Its E-Maj ratio versus relaxed was 0.949 with a
  round-paired 95% interval [0.797, 1.110]: no clear improvement in this screen.
- JIT-off and autovacuum-off E-Maj ratios versus relaxed were 1.007 and 0.983;
  both intervals include 1. Reference-profile variation was also substantial.
  Do not credit those toggles with a demonstrated speedup or slowdown.
- Observed checkpoint and workload/E-Maj-table autovacuum count deltas were zero
  in every measured interval. Deferring checkpoints therefore has no demonstrated
  saving here. Statistics are server observations, not a full maintenance trace.
- UNLOGGED tables cannot participate in E-Maj rollbackable groups. Disabling
  E-Maj's own change logging would remove the information needed for undo.

Normal application sessions retain constraints and origin/read-committed defaults;
the real workload verifies committed visibility, identity values and FK rejection.
E-Maj internally manages triggers/FK checking during administrative undo and restores
the settings. This workload has no application trigger or isolation-anomaly test;
it does not qualify those broader compatibility guarantees. DDL rollback, arbitrary
sequences, large baselines and indefinitely reused databases are outside this result.

## Method and identity

[Frozen protocol](../../../../../experiments/tt034/README.md) and
[driver](../../../../../experiments/tt034/run.py) describe the executable boundary.
Measured checkpoint: `a822c75a29d2dfd1f864defab3cae0aecddc669d`, following source
checkpoint `0162fc7`. These commits remain in history. PostgreSQL 16.15 uses image
digest `sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`;
actual local image ID is captured in every receipt. E-Maj 5.0.1 is the official
PGXN release, ZIP SHA256
`2c0fbf993b1612337054c7c3a824f7abab02f126d9eff4e6aefd17fd56c81b0c`.
Respawn 7.0.0 is lockfile-pinned. SDK/runtime and all executed DLL/JSON hashes are
in the raw input manifest; source and executable hashes were checked before and
after every case. The analysis script was added after measurement began and is
not an input to the frozen execution driver.

There is one fresh PostgreSQL container/database per run, 256 MiB memory, no CPU
quota, three pooled application connections and one administrator connection.
Pairs alternate contender order; profile order rotates each round. E-Maj uses
standard CREATE EXTENSION installation to enable its supported optimized undo
path; installing only its standalone SQL script would miss that optimization.

The shared TT-032 SQL commits writes through two sessions, then checks them through
a third. Count pooled opens/returns, all application writes and assertions, and
complete synchronous undo/reset. There are no per-cycle baseline scans or hashes.
Full seeded-row and next-ID checks occur in qualification and once after each
batch, outside SuiteMs. Container preparation, database preparation/qualification,
five warmup cycles, final audit and disposal are separate. Container preparation
included one 35.5 s attempt; it is retained and excluded from warm suite timings
for both contenders. These totals cannot be compared directly with TT-032/033's
different four-worker measurement boundary.

## Failures, validation and cleanup

Five smoke containers preceded measurement. Two attempts failed and remain in the
archive: the original sequence oracle required exact last_value/is_called identity,
and a replacement diagnostic incorrectly called row_to_json on a sequence. E-Maj's
ALTER SEQUENCE RESTART restores (1001,false), (2001,false), (3001,false), whereas
Respawn's seed input sets (1000,true), (2000,true), (3000,true). Both generate the
same next IDs. The scoped oracle compares exact seeded rows and next IDs without
writing normalization values; repeated actual inserts independently check the ID
ranges. The final smoke passed both contenders. Earlier smoke source/binary bytes
were not retained in full; their hashes, errors and diagnostics are retained, so
do not claim those exploratory attempts are fully checkpoint-reproducible.

An independent premeasurement audit led to a final audit outside SuiteMs, stronger
uncertain-create/volume cleanup handling, per-case input/executable binding and
explicit observation checks. All 40 measured attempts succeeded without repairs
or retries. The Release spike build passed with zero warnings/errors; Python
syntax and complete analysis validation passed. No production, maintained-test or
workflow files changed. Local maintained-suite and mutation runs are not claimed;
hosted repository checks remain a publication/merge gate.

All 45 owned containers and their 45 volumes were removed; each receipt verifies
absence. Final inventory preserved the two unrelated pre-existing stopped/created
containers. Source, raw evidence, release download and small build outputs are
retained; no shared cache deletion or host filesystem changes occurred.

The initial inventory was observed in session tool output, not retained as a raw
file. A later [final inventory](resource-inventory.json) explicitly records that
provenance and rechecks absence of all 77 resources from both phases.

[raw-evidence.tar.gz](raw-evidence.tar.gz) contains 236 verified members, including
all reports, receipts, PostgreSQL logs, input manifests and build/method records.
Archive SHA256: `f849c38fc2e78522e482d3375e37c561b291c700cce6045dbe523fed559b51dd`.
[validation.json](validation.json) records the member hashes and validation scope.

## Docker question and possible next control

This experiment does **not** isolate Docker overhead. A follow-up read-only check
found native Linux Docker Engine on Pop!_OS, overlay2 for the image filesystem,
and PostgreSQL data on Docker volumes backed by ext4/NVMe. The data volume bypasses
the writable overlay layer. The 256 MiB cgroup limit and bridged host-port network
path were not isolated by the original screen.
Post-process Docker stats are point observations, not memory-pressure or peak traces.
Shared-host load/free-space observations are retained, not used to discard cases.

The smallest next control would reuse the script and change one factor at a time:
256 MiB versus 1 GiB with pressure/swap observations, then bridge versus host
networking at the same memory setting. A same-version native PostgreSQL comparison
would isolate remaining container effects. Dan subsequently accepted the bounded
memory/network controls; their [supplement](docker-report.md) retains 30 additional
successful measurements and limitations. No native comparison was performed.
No result here proves Docker is the
cause, or proves its overhead is zero.

Dan then requested a [Respawn source study](respawn-study.md). It identifies the
cached native TRUNCATE path and additional repeated work in the alternatives,
without assigning unmeasured causal shares or implementing a new fast mode.

The conversation goal API refused replacement of the unfinished paused SQL Server
goal. TT-034 was executed under Dan's explicit authorization and recorded on GitHub;
the SQL Server goal was neither resumed nor falsely marked complete.

## Primary references

- [E-Maj installation](https://emaj.readthedocs.io/en/latest/install.html)
- [E-Maj rollback behavior](https://emaj.readthedocs.io/en/latest/rollbackDetails.html)
- [E-Maj limits](https://emaj.readthedocs.io/en/latest/limits.html)
- [PostgreSQL non-durable settings](https://www.postgresql.org/docs/16/non-durability.html)
- [PostgreSQL WAL settings](https://www.postgresql.org/docs/16/runtime-config-wal.html)
- [PostgreSQL sequence restart](https://www.postgresql.org/docs/16/sql-altersequence.html)
- [Docker volumes](https://docs.docker.com/engine/storage/volumes/)
- [Docker host networking](https://docs.docker.com/engine/network/drivers/host/)
- [Docker resource limits](https://docs.docker.com/engine/containers/resource_constraints/)

# TT-033: compiled restoration feasibility result

[Authoritative issue](https://github.com/DanMarshall909/TurboTest/issues/27) ·
[Frozen protocol](../../../../../experiments/tt032/TT033.md) ·
[Full numerical results](summary.json) · [Raw evidence](raw-results.tar.gz) ·
[Evidence inventory](evidence-manifest.json) · [Repository validation](validation.json)

**Stop this candidate.** The cached restoration-command prototype passed its
correctness qualifications but was slower than matched Respawn in every pair.
It does not establish the scoped 2× result. No additional performance variant
was attempted, and no production behavior changed.

## Matched result

Native Linux, PostgreSQL 16.15, relaxed running durability, warm service,
1,000 tests, four workers, four database slots and four restoration workers.
Each independent process/container performs four warmup cycles before timing.
The primary time includes every test, restoration/replenishment and the final
independent baseline/capacity audit. Preparation and service destruction remain
separately reported. All ten runs succeeded with exact cleanup.

| Measure | Respawn + seed/sequence restoration | Cached-command candidate |
| --- | ---: | ---: |
| Median settled suite | 6,767.706 ms | 9,217.400 ms |
| Median bootstrap 95% interval | 6,050.416–6,909.983 ms | 9,186.749–9,327.770 ms |
| Largest run / descriptive p95 | 6,909.983 ms | 9,327.770 ms |
| Successful recorded cycles | 5,000 | 5,000 |

Candidate/reference ratio of medians: **1.361968**, with paired bootstrap 95%
interval **[1.330042, 1.541674]**. The candidate took about **36.2% longer**.
The predeclared success condition required an upper bound no greater than 0.5.
The contemporaneous half-time target is **3,383.853 ms**; the earlier 3,233.359 ms
number from TT-032 was a planning reference, not the pass threshold for this phase.

| Pair | Order | Respawn ms | Candidate ms |
| --- | --- | ---: | ---: |
| 0 | Respawn, candidate | 6,847.593 | 9,322.844 |
| 1 | Candidate, Respawn | 6,767.706 | 9,186.749 |
| 2 | Respawn, candidate | 6,909.983 | 9,190.568 |
| 3 | Candidate, Respawn | 6,050.416 | 9,327.770 |
| 4 | Respawn, candidate | 6,698.183 | 9,217.400 |

The interval enumerates all 3,125 paired bootstrap resamples of five independent
rounds, preserving each pair. It is a small local experiment, not a population
or product-wide guarantee. Operation samples are descriptive, not thousands of
independent suite replicates. No failures were dropped or retried.

## What was tested

The candidate retains bounded workload connections and executes one cached
Npgsql command containing an explicit transaction, dependency-ordered DELETEs,
deterministic seed INSERTs and exact sequence restoration. It uses the same
declared seed input as the Respawn adapter. The normal command is 469 UTF-8 bytes;
the qualification-only fault variant is 613 bytes. Both are retained in the
candidate. Construction took 0.0333–0.0501 ms during counted preparation. These
are text sizes, not measurements of managed heap retention.

“Compiled” means generated/cached SQL text. This prototype is specific to the
declared workload; it is not a generic schema/seed serializer or a server-prepared
query. One command execution is not a measured claim about network round trips.
The result evaluates deletion strategy, transaction boundaries and command
packaging together; it does not isolate batching as a cause.

The fixed-schema, data-only contract preserves the existing application SQL,
committed work through multiple physical connections, independent exact seed and
sequence assertions, isolation, bounds and settled-capacity audit. SchemaVersion
remains immutable, as in the reference. Explicit schema invalidation causes
native replacement. TurboTest's broader automatic schema/catalog detection is
outside this comparison; no public API or production reset implementation changed.

## Correctness and failure evidence

The shared qualification passed 14 checks for Respawn and 15 for the candidate,
including fresh/cached/idempotent construction, repeated/reordered concurrent
work, deliberate seed/sequence/workload corruption, failed tests, canceled
admission and SQL, schema replacement, stale contexts/backends, resource ceilings,
server loss, final resource absence and fresh-fixture recovery. Both contenders
reused the same twelve workload backend identities across the connection-reuse
probe while retaining the four-per-database bound.

The candidate's extra probe committed a workload mutation, attempted restoration,
set a sequence to **9001**, then raised the named `P0001` error
`TT033_InjectedAfterSequence` before COMMIT. After ROLLBACK the observed sequence
was still **9001**. The failed slot remained unavailable until its source was
disposed and its database replaced. The probe verified old OID/backend absence,
exact restored capacity and successful workload execution on the replacement.
Both original and replacement errors are retained if recovery itself fails.

That deliberate qualification recovery is separate from performance eligibility.
All ten ordinary runs recorded zero reset failures and zero replacements,
including warmups. The driver rejects an ordinary recovered failure as successful
performance evidence. All fourteen owned containers and anonymous volumes across
qualification, fresh recovery and measurement were removed; the unrelated
pre-existing Docker inventory remained unchanged.

Twenty-one calibration controls cover the fixed schedule, ratio calculation,
wrong counts/settings/images, missing samples/capacity, recovered-failure
rejection, cleanup failures, wrong qualification contender, changed/missing
qualification evidence and Python syntax. An early read-only review found the
last two provenance gaps; both were corrected before the frozen measured commit.
There were no failed builds, qualification attempts or performance runs.

## Where the time went

| Descriptive observation | Respawn | Candidate |
| --- | ---: | ---: |
| Per-run median reset execution range | 9.456–9.871 ms | 27.577–28.946 ms |
| Pooled median acquisition | 9.612 ms | 27.882 ms |
| Pooled median application/checking | 8.160 ms | 7.499 ms |
| Median final settlement | 32.780 ms | 37.863 ms |
| Median final audit | 105.023 ms | 107.808 ms |
| Median preparation | 3,520.520 ms | 1,972.148 ms |
| Median warmup | 240.334 ms | 647.278 ms |
| Median final drain | 496.104 ms | 357.633 ms |
| Median complete process wall | 11,426.701 ms | 12,683.938 ms |

Reset figures exclude the four warmup returns. Queueing for restoration permits
was negligible; waiting to acquire a restored slot was longer for the candidate.
These observations locate additional time in restoration but do not identify its
PostgreSQL cause. Concurrent component totals and separately computed medians
cannot be added or subtracted to predict a new wall time.

| Successful-run resource observation | Respawn | Candidate |
| --- | ---: | ---: |
| Client sampled working-set peak | 143.06 MiB | 142.87 MiB |
| PostgreSQL sampled container-usage peak | 255.30 MiB | 255.88 MiB |
| PostgreSQL allocation growth | 66,372–66,388 KiB | 98,636–98,708 KiB |

Monitoring starts after preparation. These are separate sampled peaks, not
simultaneous aggregate usage or startup maxima. Allocation includes ordinary
PostgreSQL/WAL effects and does not isolate their causes or prove indefinite
leak freedom. Both containers had the same 256 MiB limit.

## Provenance, decision and reproduction

Measured commit: `60a35485283e7cb7703de01eb443ef2c6ab1a2cb`, based on merged TT-032
at `c2ea94c5a007228562fd9d82f9440d7747e2f0c5`. The committed protocol and all timed
C# were frozen before qualification. Reports bind source files, SDK, binaries,
image identity, settings, exact commands, plan, host load and cleanup receipts.
The paired phase ran 2026-10-31 23:03:04–23:05:09 UTC (1 November in Brisbane).

SDK 10.0.301; Docker 29.7.2; the same native Linux/Ryzen 9 5900X host and ext4/NVMe
storage as TT-032. PostgreSQL image:
`postgres@sha256:721873c34ceb9f8d8fc265984940dc982404c105f19ad51be9fdc5970a6080ea`.
The lockfile pins Respawn 7.0.0. All three running durability flags were observed
off at qualification and performance boundaries. Preparation remains durable
before that transition; all adapters retain the common `--no-sync` initdb choice.
This is not a crash-durability test. Host sharing, warm image/runtime caches and
limited platform/workload scope remain limitations.

Follow the linked frozen protocol using fresh output paths. Archive paths are
relative to `artifacts/tt033`; verify every member against the inventory. Extract
into that location in a separate checkout to reproduce analysis; the analyzer
verifies the referenced qualification/recovery files as well as all ten reports.
Checkpoint history preserves the measured source. The final report does not
rewrite TT-032's earlier results or combine phases into a new leaderboard.

**Decision: stop this candidate and defer its production adoption.** Respawn
remains the measured baseline to beat for this scenario. The hypothesis that this
bounded cached-command design would provide the required speed was rejected by
the observed result. The result does not prove that every custom restoration
design must lose or that the broader 2× product goal is impossible.

A separate future investigation could profile PostgreSQL-side restoration work
on this saved candidate to explain its extra execution time before proposing
another mechanism. That work has not been started. Production adoption of
Respawn would also need a separate decision about TurboTest's broader guarantees.

| Criterion | Evidence |
| --- | --- |
| AC1 | Frozen protocol, plan, manifest, observed flags and source/image bindings |
| AC2 | Exact restoration oracles, explicit schema replacement and post-sequence failure recovery |
| AC3 | Both qualification reports, physical reuse/bounds, corruption/cancellation/server-loss probes |
| AC4 | Five alternating independent pairs; 10,000 recorded cycles; complete timing/resources/cleanup |
| AC5 | Paired analysis, negative threshold decision, archived evidence and linked validation |

Independent completion review and actual publication are recorded on the pull
request linked from issue 27. This investigation adds no maintained production
or test changes; no changed-core mutation score is claimed.

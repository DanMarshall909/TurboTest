# Sustained production-pool comparison

Investigation for [TT-038](https://github.com/DanMarshall909/TurboTest/issues/37).
The issue and Project15 own live delivery status; this record retains the experiment.

The production background-return pool reduced complete warm-suite time in all
**20 paired comparisons**, with median reductions of **10.0–38.8%** across four
test-body durations. This confirms the intended scheduling benefit for this
fixed multi-slot workload. It does not establish a universal speedup or Respawn parity.

Each case runs 256 tests on eight already prepared workload databases, with four
test workers and four reset workers. The added wait is synthetic asynchronous
waiting while holding the database lease, beyond real committed database work.
It is neither measured application-suite time nor CPU work. Every headline time
includes all 256 physical resets and restoration of all eight Ready slots.

| Added wait per test | Awaited suite median | Background suite median | Lower suite time | Background wins |
| --- | ---: | ---: | ---: | ---: |
| 0 ms | 6.189 s | 4.329 s | 30.1% | 5/5 |
| 25 ms | 7.768 s | 4.754 s | 38.8% | 5/5 |
| 100 ms | 12.282 s | 8.694 s | 29.2% | 5/5 |
| 500 ms | 37.839 s | 34.067 s | 10.0% | 5/5 |

These are ratios of five suite medians. [summary.json](summary.json) also retains
every paired ratio, raw case wall time, throughput and descriptive latency statistics.
Pointwise exact paired bootstrap intervals use all 3,125 ordered five-pair resamples,
the ratio of medians, and nearest-rank 2.5%/97.5% endpoints. Interval endpoints are
0.481–0.770, 0.588–0.650, 0.661–0.914 and 0.870–0.950 respectively. These are five
pairs on one shared host, with no multiplicity adjustment or portable guarantee.

## Complete workflow and changing bottleneck

Complete process wall includes Docker startup, all preparation, qualification,
32 warmups, measured work, final resets, baseline/sequence audit and fixture
disposal. Its median was lower at every duration, but two 100 ms process-level
pairs were marginally slower (ratios 1.0007 and 1.0024). Startup varied despite the
same configured limits. Do not attribute its variation to the return switch.

| Added wait | Awaited process median | Background process median |
| --- | ---: | ---: |
| 0 ms | 21.043 s | 17.074 s |
| 25 ms | 22.604 s | 17.071 s |
| 100 ms | 27.846 s | 23.812 s |
| 500 ms | 51.410 s | 47.555 s |

The gain comes from overlapping application work with resets. Actual pooled
test-body medians were approximately 32–35, 54–58, 130 and 528–530 ms, respectively.
Reset-execution medians were 57–64 ms. At 25 ms added wait the two costs are
comparable; at 500 ms the body dominates and hiding reset yields a smaller
percentage reduction. Reset execution itself was often slightly slower with
background overlap. No CPU or peak-memory usage was measured.

At zero added wait, background checkout median was 27.3 ms versus 0.24 ms awaited:
the next test can still wait for a clean database when reset capacity is saturated.
At 100/500 ms it was approximately 0.25 ms. Background return median was 0.065–0.166 ms,
but that is handoff latency. Its median final reset waits of 56–83 ms are included
in the primary suite endpoint. Reset validation is contained in reset execution;
the durations must not be added. Pooled per-test percentiles are descriptive,
not 1,280 independent experimental replicates.

## Matched production inputs and endpoint

Both sides use the actual public prepared-factory API. The awaited library is
bound to `6e4cce0868d5a1efd85234296a99a4c11c83724c`; the background library is bound
to implementation checkpoint `3c03d482984c688ab70cda480c9d4c9d07b9aadd`, integrated
on master at `58dd224dcadbbe7f7e1d62bae14ac988a758b301`. The retained TT-037 runtime
receipts were rechecked before use. All 35/37 production-source hashes match their
declared commits. The common 96-file harness/dependency runtime differs only in
`TurboTest.EFCore.dll`, with every runtime file checked before and after each case.
No production code, maintained benchmark, test or workflow changed for this experiment.

The unchanged maintained `BenchmarkWorkload` inserts 100 parents, 20 children and
10 notes, updates the parent seed and deletes child/note seeds. Each cycle checks
the initial baseline, exact mutation counts, marker isolation and sequence-generated
IDs. Initial simultaneous leases verify eight distinct database names/OIDs and
separate markers. Every timed reuse must retain one of those identities. Final
audits check all eight seed baselines and each sequence's exact last-value/is-called state.

The physical-reset endpoint requires exactly the expected successful `Rows`
event-400 completions, eight Ready/reserved slots and zero leased, queued,
preparing, resetting, cleanup, quarantined, parked, rewarming or retiring work.
Ready count alone is insufficient because publication precedes permit release.
Unexpected recreation fails the experiment. Fixture disposal then joins the full
background-return ledger and removes its host; its duration is retained separately.
Shutdown alone is not used to establish final resets, since it may retire terminal
queued returns. All **40 cases / 10,240 measured tests / 1,280 warmups / 12,160 total
successful row resets including audits** passed. The controller exited zero after
1,157.03 s with its owned process group stopped and the frozen contract unchanged.

The frozen schedule rotates duration groups and alternates mode order independently
for each duration across five rounds. Each duration has a 3/2 first-mode split;
opposite starting orders across durations balance the whole schedule. No result-driven
extra rounds, omitted measured cases or replacement samples were used in the v4 result.

## Environment, qualification and retained failures

Native Linux Docker at `/var/run/docker.sock`, PostgreSQL 16.15, cached image
`sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542`.
Each fresh host has a 1 GiB memory limit, 2 GiB combined memory/swap allowance,
bridge networking and unlimited CPU quota. Both modes use eight workload slots
plus two infrastructure databases, four per-database connections, four administrator
connections, preparation concurrency one and reset concurrency four. No adaptive
scaling or durability relaxation was added. These are equal configured ceilings,
not equal measured resource consumption. One-minute host load ranged from 4.35
to 14.56 on 24 logical CPUs; other host activity was uncontrolled.

Both 16-cycle native smokes passed. Both deliberate distribution-corruption probes
exited unsuccessfully with `WorkloadDistribution`, then completed cleanup. The v3/v4
native runtime hashes are identical, so this qualification is reused for v4.

One earlier harness smoke failed before measurement because EF read an `oid`
column as bigint. Casting it to bigint fixed that harness query; its failed report
and successful cleanup remain retained. The first measurement controller (v3)
also had an ordering error: rotated position cancelled pair parity, so mode order
did not alternate within each duration. Read-only review caught it in round one.
Its eight completed cases / 2,048 cycles remain visible as an order-confounded
partial attempt, excluded from the primary v4 result. It stopped before the next
native case admission through the frozen-source check, without killing an active
case. The corrected v4 schedule preserved all sample counts and workload controls
and was independently checked before starting. Neither failure is relabelled passing.

All **53 owned containers and 53 owned volumes** across all attempts are absent.
Every native case settled its resources, all case process groups stopped, and the
entire starting Docker inventory is unchanged. Unrelated containers, volumes,
shared caches and reusable runtimes were preserved.

## Evidence, reproduction and recovery

[validation.json](validation.json) indexes every attempt and settlement.
[raw-evidence.tar.gz](raw-evidence.tar.gz) retains exact harness sources, all runtime
versions, the v3/v4 contracts, all reports, timelines, build/process receipts,
resource observations and offline analysis. [raw-manifest.json](raw-manifest.json)
hashes every member; [archive.json](archive.json) records the archive digest and
post-archive member verification. Content-identical files share bytes inside the
archive; source/runtime files on disk were independent throughout measurement.
Only redundant build caches and Python bytecode are excluded.

Extract the archive into a fresh directory and run `python3 <directory>/analyze.py <directory>`.
It rehashes frozen sources/runtimes, verifies every case/reset/identity/inventory,
checks the alternating schedule and reproduces the summary without a database.
The one-off native controller and project are also retained. Rebuilds require the
referenced TT-037 runtime inputs; reruns must use fresh output paths and a newly
frozen contract rather than overwriting existing attempts.

Raw working evidence is about 256 MiB before the archive, within the 2 GiB retention
budget. The storage tool completed the task-root scan; its optional Git category
discovery was unavailable, so a plain Git worktree inventory independently established
the one clean primary checkout. No deletion of reusable state was needed.

The existing source-identical master correctness/pack gate passed 554 tests in
[hosted CI](https://github.com/DanMarshall909/TurboTest/actions/runs/41394787441).
This isolated measurement adds native qualification and offline analysis evidence;
it makes no new maintained-suite coverage or mutation claim. TT-037's retained
coverage qualifications and pending mutation prerequisites remain unchanged.
Publication/review outcomes belong to the linked issue. No release or new backend
is included; the last working implementation remains the merged TT-037 source.

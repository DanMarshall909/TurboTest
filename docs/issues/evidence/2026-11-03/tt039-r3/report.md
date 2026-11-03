# Whole-path scheduler reference qualification

Investigation for [TT-039](https://github.com/DanMarshall909/TurboTest/issues/38).
GitHub owns live workflow status. **Revision 3 failed prospective prediction
qualification; policy search remains gated.** All 16 new native cases / 4,096
measured tests passed correctness, trace integrity and cleanup.

This revision tests whether keeping complete observed service paths predicts
new repeated runs of the same instrumented warm pipeline. The
[first attempt](../tt039/report.md) and [revision 2](../tt039-r2/report.md) remain
unchanged negative results. No thresholds, historical labels or results were
rewritten, and no scheduler policy was selected.

## Prospective result

Awaited/100 ms fails the unchanged suite gate: the stratum is underpredicted by
11.85% (10% allowance), and `r6-w100-awaited` by 20.72% (20% allowance).
All other stratum metrics and all other individual suites pass. Overall, 63 of
64 stratum metrics and 15 of 16 individual suites pass. The conjunction fails;
no model, split, sampling or limit was changed after these observations.

| Mode | Added wait | Observed suite | Predicted suite | Signed error | Suite gate |
| --- | ---: | ---: | ---: | ---: | --- |
| Awaited | 0 ms | 6.160 s | 6.260 s | +1.61% | Pass |
| Awaited | 25 ms | 7.354 s | 7.569 s | +2.93% | Pass |
| Awaited | 100 ms | 13.914 s | 12.265 s | -11.85% | Fail |
| Awaited | 500 ms | 37.525 s | 37.577 s | +0.14% | Pass |
| Background | 0 ms | 4.163 s | 4.217 s | +1.29% | Pass |
| Background | 25 ms | 4.661 s | 4.506 s | -3.31% | Pass |
| Background | 100 ms | 9.199 s | 8.697 s | -5.45% | Pass |
| Background | 500 ms | 33.934 s | 34.051 s | +0.34% | Pass |

[Complete evaluation](evaluation.json) retains every signed error, historical
role, unmodelled cost and physical/wrapper/settlement drain. The retained fit
and [forecast](forecast.json) reproduce the original negative result. A written
evaluation with exit 2 is a completed failed gate, not an input/usage error.

## Post-result investigation

The missed run was already slower during preflight: its mean warmup reset was
104.72 ms versus 61.83 ms in the other fresh awaited/100 ms run; measured reset
means were 93.42 versus 59.41 ms. Mean measured bodies were 146.12 versus
133.24 ms, each including the same 100 ms synthetic wait. The five historical
awaited/100 ms runs had measured reset means approximately 57–60 ms. These
facts indicate run-level cost variation; they do not identify a CPU/I/O cause.

The frozen R3 model predicts every new run from the same historical stratum
distribution, without using its preflight state. Preserving paths alone did
not establish transfer into this slower run. Preflight-conditioned service
estimation is a concrete next hypothesis, requiring its own declared protocol,
input cutoff and fresh validation. The existing R3 validation is now observed
development information and cannot remain a fresh holdout. The R3 failure
remains unchanged; no selective rerun, dropped outlier or policy search follows.

A read-only check across all 56 now-observed runs limits that hypothesis. After
removing stratum means, the last-16-warmup reset mean correlates 0.703 with the
measured reset mean; excluding the missed run reduces this to 0.435. The warm
binding-to-dirty body/context proxy has little corresponding relationship with
measured body minus nominal wait. Warmups use zero added wait, and their costs
are nonstationary. These observations do not support a universal speed scaler.
Subtracting the nominal wait also retains timer and continuation delays; it
does not identify database execution demand.

The current native executable writes its report after measurement. Reading
warmup fields from that final report would remain post-hoc. A prospective
conditional forecast needs a separately published warmup-only record and an
explicit forecast cutoff before the first measured request. A new barrier would
change the preflight-to-measurement transition and must itself be recorded.

## Frozen design

[Protocol](../../../../../benchmarks/scheduler-simulator/protocol-r3.md),
[case manifest](../../../../../experiments/tt039/contract-r3.json), sampler and
[native/training binding](../../../../../experiments/tt039/collection-r3-binding.json)
were committed at `1166be98f61f0ce5338c947ae56bb5b20fbb5843` before fitting.
[All forecasts and 114 input bindings](../../../../../benchmarks/scheduler-simulator/forecast-r3-binding.json)
were committed at `0f3efc1ffca22eeea380c3a2292055181d201920` before any new
validation case. Fitting took 0.615 s; all baseline forecasts and six sensitivity
probes took 3.489 s.

All 40 now-observed R2 runs supply training data, five whole paths for each
mode/wait stratum. Their original 24-calibration/16-validation roles remain
unchanged in the R2 archive. A separate R3 manifest explicitly projects them to
known training; they are not a fresh holdout. Only the two newly collected paired
rounds 5 and 6 assess this revision: 16 cases / 4,096 measured tests.

The workload and native executable are unchanged: eight databases, four test
workers, four reset permits, one preparer, four administrator connections, four
connections/database, one 1 GiB PostgreSQL 16 Alpine host, 32 warmups and 256
measured tests per case. The additional async waits remain 0/25/100/500 ms alongside
the same database mutation/validation work. Cases use the frozen rotated wait
order and alternating mode order. Two new native routing smokes passed with
complete resource settlement. Source-identical R2 held-reset/failure controls
remain reusable evidence.

The native runtime is a pinned read-only input reused from R2 through a symlink,
with bytes checked before and after every case. This is not mutation sandbox
isolation. Native sources and image are unchanged. The kernel and R2 adapter
remain unchanged, keeping the earlier archive's exact reproduction intact.

## What changed

R2 selected one whole run but then independently resampled each worker's blocks.
R3 retains every selected run's original per-worker record order and initial
offsets. Each record still links body, reset execution, cleanup, receipt,
publication, release and wrapper completion. The unchanged event kernel
regenerates admission, clean-slot and reset queues; observed queue/suite times
are never sampled as service, and observed timestamps are not forced onto the
virtual executor.

Sixty-four fixed seed selections choose among only **five distinct source
paths per stratum**. They do not create 64 independent observations. Repeated
deterministic path results are cached within each prediction call, without
sharing writable results between replicates. Medians and descriptive 5th/95th
bounds retain the original gate interpretation, not calibrated confidence
intervals.

The unchanged requirements are 10% stratum/20% individual suite error;
mean checkout/reset/clean queue max(3 ms,25%); p95 checkout/reset queue
max(10 ms,25%); legacy checkout proxy and actual clean-starvation fraction
0.10 absolute. Every stratum metric and every individual suite must pass.
The sixth sensitivity now explicitly samples independent worker records; it is
outside the primary whole-path forecast, which has no block-size parameter.

## Development evidence and limits

A labelled post-hoc R2 diagnostic using each run's own services reconstructs
suite time within 0.73% and clean starvation within 1.18 percentage points.
A further development check reconstructs all eight gated metrics within their
allowances. Both checks use observed services, including former validation, and
are not forecasts or qualification. They motivate the new sampling hypothesis;
they do not prove that lost path alignment caused every R2 error.

Any qualification applies only to repeated runs of this fixed instrumented
workload and configuration. Service durations remain conditioned on native
scheduling. Five training paths and two fresh validation runs per stratum are
limited evidence. Host variability and separate CPU/I/O demand remain unresolved;
do not extrapolate these services to arbitrary concurrency or claim
counterfactual policy rankings. Startup, preparation/warmup, final audit,
disposal and complete process wall are observed, unmodelled costs. Physical
reset drain, wrapper drain and settlement tail remain separate observations.

No runtime controller, public metadata API, new reset backend, policy sweep,
physical-finalist set or release is selected by this experiment.

## Verification and reproduction

The [simulator README](../../../../../benchmarks/scheduler-simulator/README.md)
documents all fast/slow selections. New owning tests use independently calculated
two-worker paths: 24 ms background versus 23 ms awaited for one path, doubled
for a second path, including checkout/reset queue tails and initial offsets.
They reject reordered/hybrid paths through observable timing outcomes. Fitting
preserves every historical role, excludes all new validation files, and refuses
edited fit/forecast data. A synthetic ten-times-slower fresh cohort fails against
the original frozen forecast.

The collector's freeze checks reject omitted forecast hashes, missing files,
changed source and changed forecast data. Boundary checks exercise the real
published R2 training archive and real temporary Git commits; native routing
smokes calibrate the reused process boundary.

The [raw archive](raw-evidence.tar.gz) retains the immutable attempt, all native
cases, original training roles, source snapshots, process receipts and labelled
development evidence. All 387 member hashes in the [manifest](evidence-manifest.json)
verify; 124 pinned JSON inputs suffice to rebuild the complete fit and replay
the saved evaluation. Compiled runtime bytes remain retained locally under their
R2 identity; no Docker or .NET installation is needed for archive replay.

All 26 fast and seven slow tests pass. The complete R3 archive contract rebuilds
the fit and reproduces every evaluation field except runtime; both prior
negative archives still replay unchanged. [Verification receipts and source-bound
line attribution](verification.json) retain commands, exits and timing. The
three slow selections took 13.943/199.904/61.077 s under line tracing; these are
instrumented timings, not ordinary test runtimes.

The 26 fast tests pass in 0.769 s without instrumentation (6.352 s under line
tracing). Fast line execution covers 96.6% of the unchanged kernel and 82.1% of
the whole-path module; all its uncovered lines are the command entrypoint,
which has separate subprocess success/failure contracts. This is execution
coverage, not branch or assertion coverage. The collector's fast tests cover
freeze guards; native collection and real archive/Git boundary checks provide
separate evidence for preparation/routing. No claim of complete collector
branch coverage is made.

[Cleanup](cleanup.json) verifies all 16 measured and two smoke processes stopped,
all per-case container/volume inventories match, and all 18 owned container
prefixes are absent. Runtime and shared caches remain retained. The output and
temporary filesystem had 116 GB free against the 5 GiB verification allowance;
no retained builds or caches were removed. Safe copy-on-write remains unavailable
on this same volume, so post-session mutation remains pending with no score or
unsafe fallback.

Independent engineering review and publication are recorded on the linked
GitHub issue. A passing engineering check reproduces this negative experiment;
it does not qualify its forecasts or complete the continuing qualification goal.

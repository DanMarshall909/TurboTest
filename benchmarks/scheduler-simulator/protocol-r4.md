# TT-039 revision 4: fresh calibration and fixed-cohort validation

This protocol defines a new qualification attempt, not a passing result.
R1/R2/R3 remain failed and immutable. Earlier performance measurements are
historical development evidence only; none supplies this attempt's fitted costs.

Dan requires all other work first and explicit confirmation before performance
measurement because other coding sessions share the computer. Finish source,
functional checks, review, source/runtime/contract bindings and the complete
rerun plan first. Then request the measurement window. No approval is inferred
from this protocol, a test fixture or scope agreement.

The approved-window record must name Dan's actual confirmation, this source
checkpoint, TT-038 and TT-039, and its expiry. Missing, expired, changed-source
or withdrawn confirmation blocks new timed work. A stop marker ends admission
between cases; an interrupted cohort stays incomplete and cannot qualify.

Keep forecasts fixed before all validation starts. Do not use preflight/current
validation outcomes to fit, update or select the predictor. Keep all eight
mode/wait strata, the exact R2 native executable/image/capacity/workload and
32 zero-wait warmups plus 256 measured tests per case.

Collect seven entirely new, complete paired calibration rounds: 56 cases,
seven paths in each mode/wait stratum, 256 measured tests and 32 warmups each.
Rotate the four waits by round and alternate mode order by round. Preserve every
earlier archive byte and role; do not relabel old measurements as fresh inputs.
Fit only these new calibration reports. The model is an empirical mixture of
whole service paths with original worker order and initial offsets. No outliers,
independent components or hybrid worker paths enter the baseline forecast.

Point rule: enumerate each of seven complete replay paths exactly once
per scenario, and report their equal-weight arithmetic mean for each metric.
This is an expected-metric point forecast. It is not claimed to equal the
population median. Retain descriptive empirical path bounds and support count.
The original observed statistic remains the across-run median, with all original
absolute/relative allowances; every individual suite must still meet 20%.
Require BOTH the original gate on the first two fresh visits to every stratum
AND the same gate over the entire prescribed cohort. No result from later
visits can rescue a failed first-two gate. Every one of 84 individual suites
must pass. The whole protocol is a conservative extension of that original
two-visit challenge, not an unchanged finite-cohort procedure.
Any resulting PASS demonstrates the stated empirical approximation at those
unchanged tolerances, not equality of mean and median or general policy ranking.
Averaged run-level p95 metrics are not pooled observation p95s.

Development reason for replication: the old /25 background two-round observed median can vary
more than its 0.10 absolute allowance. Conditional on exchangeability/independence
with those seven historical paths, an exact order-statistic calculation gave 96.33%
probability for the two starvation metrics at 21 fresh repetitions under the
development point. This is an illustration, NOT a population-power,
confidence, coverage or all-gate guarantee. Seven source paths and temporal
drift remain unresolved limits. NIST's median-interval treatment uses binomial
order statistics: [NIST median confidence limits](https://www.itl.nist.gov/div898/software/dataplot/refman1/auxillar/mediancl.htm).

Fixed schedule: 21 epochs numbered 7–27. Every epoch includes both
modes at /25. One extra wait rotates 0/100/500 by epoch, so each other mode/wait
has seven cases spread over the experiment. For zero-based epoch index `i`,
visit /25 first for even `i`, second for odd `i`. For zero-based wait position
`p`, use awaited/background order when `(i // 2 + p) % 2 == 0`, reversed otherwise.
This crosses /25 position and mode order over four-epoch blocks. The complete
case manifest records every identity and visit explicitly.
Exactly 84 fresh validation cases / 21,504 measured tests follow the 56 fresh
calibration cases / 14,336 measured tests. All 140 case identities, roles and
order freeze before calibration. No adaptive stopping for success, added cases, dropped
cases, selective reruns or threshold/model changes after observation.

Finish the entire cohort despite prediction misses. Evaluate both gates only
after all prescribed native outcomes exist. Any correctness/cleanup failure or
resource-cap stop retains an incomplete attempt; it cannot qualify from a subset
and receives no replacement cases. Freeze source/protocol and both cohort
manifests in a commit before timing controls or calibration. After the approved
native controls, commit collection-ready bindings before fresh calibration.
Then fit only calibration and commit every fit/forecast/input binding before
any validation case. Never consume validation reports while fitting.

Report every error at the unchanged gate and every individual suite result.
Retain separate startup/preflight/audit/disposal costs, physical/wrapper/drain
timings and source/runtime/process/resource receipts. A failed/incomplete
experiment remains failed/incomplete, with no automatic extension.

The development leave-two-rounds-out check did not establish estimator
superiority: exact mean passed 7/21 overlapping folds, exact median 8/21;
each had six individual-suite failures. All-observed suite coverage includes
the previously missed run in training and is not new predictive evidence.

The full requested rerun also includes TT-038's complete v4 baseline comparison:
40 fresh cases / 10,240 measured tests and all four native qualification controls.
Use its exact retained common harness and two historical production libraries,
which differ only in TurboTest.EFCore.dll. This is distinct from TT-039's traced
single-library awaited/background comparison. Keep their results separate.
Recheck TT-039's four smoke/failure controls, both held-reset controls, the
enabled/disabled probe-cost control and all six on/off instrumentation cases.
All these timed entrypoints share the explicit owner-confirmation guard.

Resource envelope: one serial native producer, same one-host PostgreSQL limits,
two-hour collection cap, per-case 300-second supervisor. Before each case reserve
480 seconds of the cap for the case deadline and settlement; stop between cases
if the remaining envelope is insufficient. Keep a larger outer observer deadline so budget stopping can
settle normally. Existing competitive-baseline disk growth/reserve must fit;
new retained output budget 1 GiB; do not delete runtime/shared caches. Existing
historical runtimes guide planning only. The requested three primary cohorts
total 180 cases / 46,080 measured tests / 5,760 warmups, excluding controls.
The confirmation request must give a bounded whole-session window; no automatic
extension or replacement is permitted when it expires.

Even a PASS applies only to this fixed instrumented workload, capacity and
approved measurement context. Costs remain conditioned on native scheduling;
CPU/I/O demand is not separately identified. Seven source paths, drift and host
variability remain limits. No counterfactual policy ranking follows from the
fixed-capacity validation. Observed startup/preflight/audit/disposal and distinct
physical/wrapper/settlement drains remain explicit.

The independent design audit requires retaining the first-two challenge,
accurate labels and complete fixed-cohort stopping. Those corrections are
included here. Complete offline implementation/contract verification and an
independent readiness review before asking to start performance measurements.
No policy search, runtime controller, public metadata API, backend or release.

# TT-039 revision 3: whole-path reference protocol

Question: can a bootstrap over complete, observed per-worker service paths
predict new repeated runs of the same instrumented workload/configuration?
Preserve R2's FAIL and all historical labels. Do not retune its gate.

## Development evidence and hypothesis

R2's post-hoc own-service reconstruction has <=0.73% suite error and <=1.18
percentage-point clean-starvation error. A further development diagnostic
reconstructs all eight gated metrics within their existing allowances. These
checks use observed services, including old validation, and are not forecasts.
They justify testing a service-sampling hypothesis, not claiming a cause or
prospective accuracy.

R3 removes independent per-worker block resampling. Select one complete source
run for each replicate. Retain all four workers' records in original ordinal
order and original initial offsets; regenerate admission/reset queues through
the unchanged integer-microsecond model. Never force observed timestamps or
sample observed queue/suite durations as service. This preserves aligned
observed service paths, not a guaranteed native wall-clock chronology.

## Fixed training and fresh holdout

Declare all 40 R2 runs (five complete paired rounds, five paths per mode/wait)
as known R3 development/training data. Keep their R2 calibration/validation
labels unchanged in the original archive; project their new role only in the
new R3 manifest. Pin their source archive and every training report/receipt.
No outlier deletion or run selection. No new native calibration runs.

Collect exactly two fresh paired rounds, numbered 5 and 6, for validation:
16 cases, both modes at 0/25/100/500 ms, 256 tests and 32 warmups each.
Use the original rotated-wait rule (round modulo four) and alternating
within-pair mode order. Freeze the exact case list and sources before fitting;
freeze all forecasts and training/source hashes before collecting any of these
16 cases. New validation data never enter fitting or model selection.

Reuse the exact pinned R2 experimental executable and PostgreSQL image; no
native source/build changes. Eight slots, four test workers, four reset permits,
one preparer, four administrator connections, four connections/database,
one 1 GiB PostgreSQL host; unchanged mutation and per-cycle assertions.
Two new small native smokes verify the new collector's root/binding routing.
Freeze this protocol and the sampler before fitting actual R3 training data;
unit fixtures are independent synthetic controls, not fitted measurements.
Existing source-identical held/failure controls remain reusable evidence.

## Forecast and gate

Use seeds 39000-39063 and uniform source-run selection per stratum. Sixty-four
replicates have only FIVE distinct source paths; report that support explicitly.
No circular starts, blocks or independent component draws in the baseline.
Keep every R2 metric and allowance: 10% stratum suite, 20% individual suite;
mean checkout/reset/clean queue max(3 ms,25%); p95 checkout/reset queue
max(10 ms,25%); legacy proxy and actual starvation fraction 0.10 absolute.
Every stratum metric and every individual suite must pass.

Report medians and descriptive 5th/95th bounds, not calibrated confidence
intervals. Preserve observed unmodelled costs and the physical/wrapper/settlement
drain decomposition. Keep the same six sensitivities: body x0.8/1.2, reset
x0.8/1.2, extra release tail 2 ms, and a clearly labelled independent-worker
single-record sampling comparison. The latter removes path alignment and is
outside the baseline forecast; do not call it a baseline block-size parameter.

## Implementation and stopping

Keep model.py, revised.py, reference.py and protocol-r2.md unchanged so the
exact R2 archive replay remains valid. Add a separate sampler/adapter using
read_trace, assess, metric helpers and simulate. Its own manifest must express
40 training and 16 fresh validation cases; do not monkeypatch R2's 3+2 split.
Use the established process supervisor, reader checks and per-case identity /
resource settlement with a new output root. Add tested fail-closed freezes.
Treat the pinned native executable as a shared read-only input, not a CoW
mutation sandbox; verify its bytes before/after every case.

No extra rounds, result-driven reruns, threshold changes or sampler changes
after observing new validation. Retain any failed attempt and all diagnostics.
If the gate fails, qualification remains unsuccessful. A further revision
needs a new declared protocol and new held-out runs. If it passes, the claim
is only repeated-run prediction of this fixed instrumented workload. Five
training paths/two validation runs, host variability, unmeasured CPU/I/O demand,
native-schedule-conditioned services and unsupported counterfactual rankings
remain limitations. No policy sweep, finalist set, runtime controller, public
metadata API, new reset backend or release is selected here.

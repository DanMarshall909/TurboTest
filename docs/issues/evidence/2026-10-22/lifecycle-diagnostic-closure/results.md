# Lifecycle diagnostic identity and recovery

Owning issue: [TT-023](https://github.com/DanMarshall909/TurboTest/issues/13). The [54-question disposition and eight-criterion matrix](question-dispositions.json) preserves exact original identities, statuses, test methods and current source hashes. The independent whole-ticket verdict is in `completion.json`.

## Product changes

A demonstrated startup defect hid a distinct cancellation-callback failure when the initializer already carried a child cleanup capability and the outer drain succeeded. Startup now tracks whether its own retained drain owns callback reporting. Initializer and callback child capabilities remain recoverable together. Interrupted startup consistently reports callback failure through retained recovery, preventing initial/recovery duplication.

Maintenance workers release completed actual success directly. Failed work stays retained until the observer or drain reports it. This removes redundant observer-ledger and Delivered dependencies and the worker's observer parameter. The source proof permits reentrant synchronization contexts: each consumer setting Delivered removes that work itself, and independent acquisition settlement remains joined before drain reads maintenance.

Documented observations distinguish initializer-owned timeout, observation deadline, caller cancellation and owner-only closure. Caller participation before observer resumption wins in either request order; actual work and unseen failures remain owned. The latest composed cleanup capability retains every remaining child in the exercised failure paths.

## Behavior and evidence

One genuine pre-repair RED row loses CallbackStage; two controls pass. The final causal-guard regression also loses the actual nested callback's42P01, with15nearby controls passing. A native three-host test independently observes two real retained child hosts and real22012/42P01 failures, then recovers both exactly once through the returned capability after successful outer deletion.

The four-error component case requires seed42P01, callback42501, drain53100 and retry53200 plus four child owners and the actual remaining host. Later retries physically clear all five owners without consuming a distinct failure. Other maintained cases cover safe/raw stages, first-error identity after fully completed second failure, abandoned delivery without drain replay, rewarm marker transfer, two actual drain failures, failed-context lifetime and documented timing event identities. Expected identities are input-derived; no arbitrary error prose/order is pinned.

Final isolated calibration compiles **25 original fault shapes and3 current regression faults** against the same final source. Every intended diagnostic rejection and passing control is retained. Original source inputs are restored and349checks pass. This is not a54fault historical Stryker replay; all original mutation statuses remain unchanged. Remaining findings use bounded current producer/source proofs and maintained executable contracts.

## Verification and design review

Final owning coverage is **348fast+35native=383passed**. The complete ordinary gate is **348fast+172PostgreSQL=520passed**, no failures or skips. Solution and separate DynamicScalingExample builds pass. Coverage, fault calibration and complete ordinary checks bind identical source/runtime/resolved dependencies. Resource inventories preserve the unrelated stopped baseline; actual owned hosts settle and command process groups stop.

Each criterion has its characterization or genuine RED, GREEN, coverage, refactor decision and canonical fast gate before the next. Existing fixtures/tables absorb cases; no new production abstraction or private membership test is added. Repeated ordinary timing evidence retains the fast/slow lanes; the nested native boundary uniquely earns its setup cost. `lane-review.json` and raw timings are archived.

## Qualifications and publication

Earlier per-criterion source cuts remain historical. The archive explicitly retains harness type-name/count/comparison errors; none is relabelled product RED. The first final comparator failed on differing fault-file stack paths in the intended assertion failure; final-v2 compares diagnostic text exactly after removing stack-frame lines. Criterion15 compares heterogeneous missing-error assertions separately. The current1924source proof supersedes an earlier overly broad nonreentry inference; prior1925weak-node evidence remains a one-off historical measurement.

All54assigned findings have dispositions and all8accepted criteria have executable or previously accepted source-bound evidence. This does not assert every hypothetical future producer/foreign-token/aggregate taxonomy. Whole-session mutation is pending the final coding-session snapshot. Native Project fields remain unverified/unmodified. Source/evidence remain local under project policy; publication closes issue13only after independent PASS and a verified local commit. No resources, caches or evidence are deleted.

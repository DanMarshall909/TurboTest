# TT-024 / TT-023: cancellation, retained state and reentrant drain

This bounded slice advances the open lifecycle issues. It does not close either
issue or replace its GitHub acceptance checklist.

## Criteria and evidence

| Contract | Owning evidence | Fault calibration / decision |
| --- | --- | --- |
| Initializer cancellation source remains usable until initialization and asynchronous cancellation callbacks settle, then releases its kernel handle | Five added StartupLateDrainTests cases: success plus caller/deadline interruption crossed with first deletion success/failure; exact deletion and reservation settlement | initializer-calibration-final: 1769, 1777, 1787, 1839 and normalized 1840 disposal faults fail lifetime assertions |
| Monitor cancellation source survives a held reader and held cancellation callback; repeated Stop/Join deliver each callback failure once | Two CapacityMonitorTests cases, throwing and nonthrowing callbacks; source alive while held and closed after both jobs settle; no later ticks | monitor-calibration: missing source disposal rejects both cases |
| Completed recovery releases registry handles, callback/resource payloads and composed child handles | Three CleanupRetentionTests facts, including 16 completed registry owners; actual completed helper scopes, bounded GC and live parent/registry roots | retention-calibration-final: independently remove registry removal, callback clear, resource clear and member clear; each intended lifetime assertion rejects its fault |
| Live caller cancellation sources cease retaining completed slot owners | Three SlotRegistrationLifetimeTests outcomes: publish, close and cancel; 16 repeated publish/close cycles; actual queue completion, confirmed deletion and zero reservations | registration-calibration-final: missing pending unregister retains all 16 corresponding owners; useful publication/failure unregisters retained |
| Self-unregister inside an executing cancellation callback has no useful effect in the current producer/runtime domain | Current sole-caller/assignment proof against pinned .NET 10.0.9 callback-node lifecycle, plus all three registration cases | self-unregister-decision.json: remove only question 1439; do not generalize to pending registrations or other APIs |
| Lease drain publishes its shared pending task before a reentrant return observer | Added LeaseOwnershipTests case: one return, one observation, pending nested/shared task, context closed, external gate and joined actual tasks | reentry-calibration: removing barrier 2096 produces two returns/observations; actual public logger responsibility modeled with a legal already-completed producer result |
| Slot drain publishes one pending task and retains its reservation until deletion confirmation | Added SlotPoolTests case: completed return command, nested drain observation, one delete, retained reservation, then one confirmation and zero reservations | reentry-calibration: removing barrier 1691 produces two deletion attempts; internal owner/deletion-adapter contract only, no established deterministic public factory counterexample |

All 15 new rows are characterization of existing supported behavior. They were
green on introduction; compiled source-substitution faults establish assertion
sensitivity. They are not represented as new production repair REDs or as new
statuses in the historical Stryker report. The production change is the single
redundant self-unregister removal supported by its bounded source proof.

Each criterion retains owning execution, source-bound coverage, fault source,
evaluated compile inputs, build/test output and process settlement. Canonical
fast gates passed after each cycle: 280, 282, 285, 288 and finally 290 checks.
Final owning coverage has 23 passing rows against the same source, compiled
inputs and resolved dependencies as the complete canonical run.

## Qualifications and retained corrections

- The first initializer collector request used unavailable XPlat coverage: tests
  passed but that invocation collected no coverage. Real built-in Code Coverage
  reports are separate and retained; no coverage is inferred from the first run.
- Raw removal of 1840 causes CS0219 because its control local becomes unused.
  That build failure is not a behavioral rejection. The normalized calibration
  preserves the control local use while removing only disposal and fails the
  intended success-path handle-release assertion.
- Initializer coverage attribution originally named closing brace 182 instead
  of disposal statement 180. Both the original and corrected inspection are
  retained; the underlying XML executed the actual disposal statement.
- Blocking helper joins run on Task.Run; GC occurs after the no-inline helper
  scope exits and actual cleanup settles. Caller sources/parent handles remain
  strongly held across collection. No reflection-only collection count oracle
  or production inspection seam was added.
- Lease barrier calibration models the legitimate observer boundary. It does
  not claim deterministic reproduction of the native public scheduling window.
  Slot calibration establishes the existing internal adapter law, without a
  public native reachability claim. Both barriers remain.
- The optional external PostgreSQL observer reads safe activity/wait categories
  on newly observed labelled containers. It adds one diagnostic connection and
  is not a performance or admin-budget proof. No raw query text, passwords or
  private PostgreSQL errors are retained by that observer.
- Historical evidence remains bound to its own recorded source cut. Final
  coverage and ordinary verification bind the final candidate inputs.
- Whole-session mutation remains pending until the final coding-session
  snapshot. Original mutation statuses are unchanged.
- Source and evidence commits are local only; no configured code remote or
  hosted CI result is claimed. Existing unknown resources and reusable caches
  are preserved.

## Remaining scope

The prior ownership slice addressed questions 2113, 2116 and 2118. This slice
adds bounded decisions/evidence for 1769, 1777, 1787, 1839, 1840, 217, 1884,
1439, 1503, 1657, 2096 and 1691. That is 15 of the 48 linked questions across
both slices, with the public reachability qualifications above. The other 33
remain unresolved. Completed factory work/context retention, dominated database
fallback, optional warm/demand domains, additional late failure orderings and
provider shutdown decisions remain under the open issues.

The next selected work is the factory completed-entry/fallback ownership proof
and the maintenance-observer-canceled, actual-failure-completed, drain-start
ordering shared with TT-023. SQL Server support remains a separate future goal
after the active ticket backlog.

Final ordinary gate: **290 fast + 168 PostgreSQL = 458 passed**, no failures/skips. Total 616.940s. Solution and separate example builds pass. Exact discovered/executed IDs and matching before/after labelled Docker inventories are retained. Final 23-row owning coverage binds the same source, binaries and dependencies. Independent verdict will be recorded separately in completion.json after review.

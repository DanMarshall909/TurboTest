# TT-024 / TT-023: factory ownership and completed work

This bounded slice advances the open lifecycle issues. GitHub owns acceptance
checklists and workflow state; neither issue is closed or archived here.

## Criteria and evidence

| Contract / decision | Implementation and owning assertions | Sensitivity / scope |
| --- | --- | --- |
| A canceled maintenance observer does not lose an actual failure that completes before drain starts | Two FactoryMaintenanceTests cases finish the underlying operation first, then start drain. Preserve the original safe stage, distinct drain failure, cached disposal identity, successful retries and exact confirmed deletion | Missing maintenance tracking rejects both rows; retaining only the first drain error rejects the dual-error row |
| A caller may abandon its acquisition task while context cleanup remains pending | FactoryBackpressureTests public context case; canceled configuration enters held asynchronous disposal; only weak references escape a no-inline helper while factory stays alive | Missing database registration 1941 permits premature finalization. Test-only rescue preserves release and actual cleanup joins; final source control passes |
| Successful repeated operations release completed context, lease and public acquisition-task references | Sixteen cycles through the same live factory; bounded collection after helper exit; another acquisition still succeeds; final drain confirms one retained physical resource deletion | Retaining completed databases or leases rejects the lifetime assertions. A bare settlement-TCS leak control still passes, explicitly leaving that separate question open |
| Final database fallback is dominated under current producers | Remove only final enumeration in factory drain. Close admission/publication; wait acquisitions; wait remaining leases. Failed acquisition discard removes its entry before settlement; lease return removes its database before its lease | Bounded source proof independently reconstructed. Retain dictionary/add/removes as useful pending-cleanup roots. Excludes fatal allocation/corruption, invalid pool producers and unsupported throwing internal clocks |

All four new maintained rows characterize behavior already supported before
their introduction; they were green on introduction. Compiled isolated faults
establish assertion sensitivity. The production refactor removes five lines of
dominated fallback and documents the distinct retained ownership root. It is not
represented as a newly repaired runtime defect or an original mutation replay.

The actual late-maintenance boundary remains covered separately by
`PreparedMaintenanceDrainTests.Canceled_retirement_reports_a_late_real_drop_failure_after_owned_cleanup`:
PostgreSQL rejects a real DROP with SQLSTATE 55006, retry drops the workload,
and Docker independently confirms the owned host is gone. The new fast law
establishes the previously missing actual-failure-before-drain ordering; the
ordinary native contracts retain physical boundary evidence.

## Root probe and repairs

The pending-lifetime probe holds no external strong release gate. Such a gate
would root the continuation graph being measured. Its context has a test-only
finalizer that records premature collection and rescues the context so even the
fault case can release and join actual work. Long weak references preserve
that recovery; correct ownership prevents finalization. Normal disposal calls
SuppressFinalize.

A bounded helper synchronization context executes the actual cleanup callback
through its held await before returning. In the current component producer flow
its queue is empty at helper exit, leaving neither a queued callback nor a worker
stack as an accidental root. Later posts forward to the thread pool. This is a
component ownership test, not a native end-to-end scheduling reproduction.

Preliminary independent review found and resolved test recovery weaknesses:
startup now lies within the cleanup scope; an abort flag releases cleanup that
starts during recovery; the actual acquisition task is acquired strongly before
the gate is released; nested finally always drains the factory. The completed
payload probe uses ordinary contexts and needs no finalizer or private reflection.

Historical setup and intermediate evidence retain their original source cuts:

- The first maintenance build compiled without warnings/errors but the driver
  rejected remaining shared compiler descendants and stopped its owned group.
  Corrected builds use the canonical node/server controls. This is setup, not RED.
- Initial root-test setup had CS0122 from outer access to a nested private member;
  the probe was moved to the outer fixture. This is a compiler correction, not RED.
- Coverage attachment and inbound copies are hash-identical and count as one run.
- Original, refactored and final calibrations remain separate. No historical
  Stryker status is relabelled; whole-session mutation remains pending until the
  final coding-session snapshot.

## Remaining work and tracker claims

This supplies bounded evidence/decisions for question 1941 and the dominated
fallback questions 2065/2067/2068/2069. Combined with the 15 decisions in the
prior ownership/lifetime slices, 28 of the 48 linked questions remain unresolved.
No blanket equivalence or whole-ticket completion is claimed.

In particular, public context/task collection does not prove that a separate
settlement TCS or MaintenanceWork node is released. The 2002 control demonstrates
this limitation. Questions 1902/1905/1906/1907/1925/1927/2002/2055 remain open.
The narrower cancellation-wins, actual-failure-recorded, observer-finally race
for 1906 is distinct from the new test's completed-observer ordering.

The proposed exact tracker updates check TT-023's completed-failure ordering,
previously verified monitor Stop/Join and multiple static/startup-owner criteria,
and TT-024's previously verified explicit/causal/secondary capability precedence.
Prior independently reviewed packets are `cleanup-ownership` (7495ccf) and
`cleanup-lifetimes` (a6dc940). All other checklist items stay unchecked. Drafts
are archived as proposals; their application requires fresh issue-body comparison
and readback. GitHub alone owns the resulting live checklist state.

Source and evidence are local only; no code remote or hosted CI result is claimed.
Unknown pre-existing stopped containers and reusable caches are preserved. The
external PostgreSQL observer reads safe wait/activity categories only and adds
a diagnostic connection; it is not performance or administrative-budget proof.
SQL Server support remains the requested future goal after the active backlog.

Final ordinary gate: **294 fast + 168 PostgreSQL = 462 passed**, no failures/skips. Total 631.462s. Solution and separate example builds pass. Exact discovered/executed IDs and matching before/after labelled Docker inventories are retained. Final 15-row owning coverage binds the same source, binaries and dependencies. Independent verdict will be recorded separately in completion.json after review.

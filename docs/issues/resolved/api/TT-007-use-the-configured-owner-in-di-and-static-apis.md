---
id: TT-007
title: Use the configured owner in DI and static APIs
status: Resolved
priority: P1
type: Bug
created: 2026-10-14
depends_on: [TT-005, TT-006]
---

# TT-007: Use the configured owner in DI and static APIs

## Problem

`AddTurboTestDbContext` calls `GetConnectionString`, which always throws. `TtServiceFactory` initializes its own pool then delegates context/factory acquisition to static `Tt`, creating a second pool and bypassing DI configuration. Static disposal also leaves initialization state pointing at the disposed pool.

Source: [service registration](../../../../src/TurboTest.EFCore/Extensions/ServiceCollectionExtensions.cs), [TtServiceFactory.cs](../../../../src/TurboTest.EFCore/TtServiceFactory.cs), [Tt.cs](../../../../src/TurboTest.EFCore/Tt.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Route DI and static conveniences through the supported lease/fixture owner. Define synchronous DI resolution versus async fixture initialization explicitly; remove or replace the unusable connection-string contract. Preserve a clear lifetime relationship between service scope, context lease and fixture.

## Acceptance criteria

- [x] The documented DI registration resolves an owned context lease and executes a query through its context; it does not call a method that unconditionally throws or give raw-context cleanup a second owner.
- [x] DI context/factory paths use exactly the configured owner, not a separately initialized pool plus the global pool. Limits, schema/seed initializer, image, Docker host and logging configuration are honored.
- [x] Leased DI registrations document `CreateAsyncScope`/`DisposeAsync`. Async scope disposal awaits lease return; async provider disposal awaits owned-resource drain under TT-006. Neither reports completion while cleanup is pending.
- [x] Synchronous scope/provider disposal has an explicit tested contract: reject async-only cleanup with an actionable use-async-disposal diagnostic, or implement a documented supported synchronous path. It cannot silently leak a lease or conceal sync-over-async blocking.
- [x] Two independent providers with different options/baselines do not share mutable fixture state or affect each other's disposal.
- [x] Static create → dispose → create either constructs a new valid owner or rejects reuse with a documented lifecycle error; it never accesses a disposed pool.
- [x] Concurrent initialization/disposal has one owner and no orphan pool. Configuration changes after initialization have a documented validated policy.
- [x] Examples describe supported initialization and lifetime usage, with no hidden sync-over-async requirement for ordinary async use.

## Verification

Use service-provider integration tests that check database identity, applied options and task-owned resource counts. Include direct/factory/DI parity and reinitialization regressions.

## Implementation progress

- **2026-10-14 — configured service owner:** The native RED returns a
  `testdb_...` database instead of the configured `di_owned_...` identity.
  Service lease/factory methods now share one provider-owned fixture per context
  type, with an options snapshot and serialized initialization/drain. The selected
  coverage run passes 24 checks; the native oracle verifies database reuse,
  configured connection limits and exactly one prefixed container. The old
  connection-string method and scoped registration remain for the next criterion.
  Coverage executes owner reuse and ordinary disposal; failure/closure races
  remain unverified. Production/test refactoring needs no additional abstraction:
  one owner dictionary and direct identity/resource assertions express this rule.
  Raw commands, results and RED source are retained under `artifacts/tt-007`.
- **Async-initialized DI contexts:** A temporary no-op declaration allowed the
  new API to compile while the existing registration still failed at its throwing
  connection-string call. The first test attempt had an incorrect identity tuple
  and is retained as a setup failure; the corrected RED fails at the expected
  unsupported method. Scope initialization now acquires one lease asynchronously;
  synchronous resolution reads only its completed context. Two native scopes query,
  write, return and reuse the same clean database. The selected coverage gate passes
  25 checks. The unusable connection-string contract is removed. Scope initialization
  publishes its task before invoking the factory, following the existing ownership
  pattern; tests retain direct scope/query/reset assertions. Early resolution,
  pending return, disposal rejection and initializer failure need separate evidence.
- **Scope lifetime and baseline parity:** Five fast checks prove early-resolution
  diagnostics, shared initialization, pending return, canceled observation and
  synchronous scope/provider rejection. Rejected framework disposal is terminal,
  so recovery uses a previously captured service owner, not another provider
  disposal call; this needs documentation and native calibration. A native RED
  finds no seed row because the registered baseline was ignored. Providers now
  use their registered `TtBaselineDefinition<TContext>` with the same snapshot.
  Eight selected checks pass: distinct seed/database identities, one host per
  provider, configured limits/image/memory, reset logging, and independent drain.
  Coverage executes these paths; initialization/drain failure still needs proof.
  Production stays on existing ordinary/prepared factory paths. Test refactoring
  makes the logger's event capture safe for concurrent callbacks.
- **Provider closure during startup:** The native RED observes a raw two-second
  wait timeout because provider closure did not interrupt its held seed initializer.
  Closure now cancels only startup observation, retains the existing TT-006 cleanup
  capability, and observes its actual work under the configured drain timeout.
  Nine selected fast/native checks pass, including exact host retention until the
  noncooperative seed settles and cached disposal failure. This closes the orphan
  gap between failed initialization and provider ownership. Production uses the
  existing cleanup capability rather than a second resource ledger; the test holds
  the actual seed and checks physical container identity. Native live-lease drain,
  static concurrency/configuration and final broad verification remain.
- **Native disposal calibration and logging:** Eight selected checks pass, including
  actual live-host retention through provider drain timeout and recovery after
  synchronous provider rejection using the captured service owner. The minimum
  logging level was ignored by both pool paths; two fast RED cases reject emitted
  Debug/Information below the configured threshold. A shared logger boundary now
  combines the fixture threshold and supplied logger policy. Five selected
  fast/native checks pass, including reset timing and configured DI baseline
  logging. Tests requesting Debug reset diagnostics now configure Debug explicitly.
  Coverage executes logger filtering/delegation; unused scope delegation remains
  an interface requirement, not a claimed exercised path. No further production
  abstraction is needed; the fast theory adds the supplied logger's suppression
  policy without duplicating native setup. README and the maintained configuration
  example now show async initialization/disposal and synchronous-rejection recovery.
- **Preliminary audit correction:** A separate read-only audit found that returning
  raw `TContext` from Microsoft DI creates a second disposal owner, independent of
  the lease. The fast resolved-scope RED confirms two context disposals. The accepted
  leased-DI contract is clarified above: resolve/inject `TtLease<TContext>` and use
  its context. Both DI and the initialization wrapper then join the lease's cached
  cleanup job. The prerelease migration is documented explicitly. The audit also
  found serial fixture closure and only the first retained failure exposed by
  provider recovery; a separate multi-owner criterion will repair that behavior.
- **Audit repairs:** The resolved-scope GREEN passes ten selected checks and
  observes exactly one context disposal through the lease contract. A two-type
  native RED confirms that the second fixture remains open while the first drain
  waits. Provider shutdown now starts all fixture closures, uses one observer
  deadline, and retains an aggregate cleanup capability/resource snapshot. The
  first repair attempt deadlocked by holding the initialization gate while joining
  a held startup job. That exact owned run was interrupted, its container identity
  was retained and removed after process termination. Closure now releases the gate
  before joining actual work, and closed lookups reject before waiting. The repaired
  selected coverage run passes ten fast/native checks, including both host IDs,
  independent release gates and complete aggregate cleanup. Production uses the
  existing cleanup capabilities and one immutable fixture/resource-view record;
  test refactoring adds a bound around the previously unbounded closed lookup.
  Native scope/provider overlap now exercises actual resolved leases. Final broad
  verification and independent completion review remain; TT-007 is still Open.
- **Multiple failure reporting:** The audit's original mixed trace assumed task
  input ordering. [Pinned .NET 10's non-generic `WhenAll`](https://github.com/dotnet/runtime/blob/v10.0.9/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs) records failed completion
  order; the first mixed test passes and does not prove that trace. A calibrated
  test then exposed an overly strict assumption that the second retry always
  succeeds; distinct late failures can require distinct observations. That oracle
  is corrected to require every material kind before completion. A stronger native
  RED with two prompt failures actually loses `IOException` from the provider
  diagnostic, and a fast RED loses both constituent kinds from aggregate
  presentation. Cleanup waits now inspect the completed aggregate's full exception
  set, preserve material failures before filtering deadlines, and safely report
  every stage/kind without payloads or inner exceptions. Eleven selected checks
  pass, including mixed timing, both prompt failures and late startup cancellation.
  Coverage and exact 88-input source bindings are retained with that gate.
  Production uses one shared await boundary and the existing safe diagnostic
  formatter; tests count all valid observations rather than forcing timer order.
  Immediate closure of previously returned factories still needs its own
  held-initialization oracle. Full-suite/build reruns and completion review remain.
- **Immediate closure:** A user-supplied baseline registration factory is held
  before startup can observe cancellation. The native RED successfully acquires
  from an existing returned factory after provider closure, proving the window.
  Provider disposal now closes already-known fixture handles synchronously before
  returning; core drain joins their cached jobs and any in-flight initialization
  separately. Eleven selected scope/drain checks pass, including this exact gate,
  later interruption and empty physical inventory. No extra ownership abstraction
  is added; the test holds a supported registration callback and observes public
  acquisition rejection. Final source covers all accepted behavior paths, subject
  to the remaining broad gate, builds, evidence reconstruction and independent
  completion review. Acceptance boxes remain unchecked pending those gates.
- **Concurrent closure publication:** The follow-up audit identifies a peer that
  returns the published disposal job before its first caller closes fixture
  handles. A narrow internal scheduling seam holds that publication window;
  the corrected native RED acquires a lease after the peer disposal returns.
  Its first run had a cleanup oracle that rethrew the cached provider failure,
  and is retained separately from the corrected behavioral RED. Every disposal
  caller now snapshots and closes known handles before returning; fixture
  disposal still shares its original job. Ten selected closure/drain/failure
  checks pass with coverage and exact source binding. This retains the existing
  ownership structure and adds no alternate disposal path. Latest fast coverage
  passes112 checks on the earlier immediate-closure cut; the final full gate
  will bind the complete peer repair. Final builds and completion review remain.

- **Full-gate startup race:** The first completed broad run passed241 checks but
  failed one catalog case at `ContainerPrepare`; an unchanged-source targeted
  rerun passed all ten catalog cases. A second broad run passed240 and failed two
  unrelated existing pool checks at the same boundary. Docker daemon timestamps
  prove host port bind collisions in all three cases. The free-port probe released
  its socket before Docker bound it. A native regression now deliberately claims
  that advertised port before container creation; its RED fails at
  `ContainerPrepare`. Docker now chooses the published port and inspection supplies
  that port to the owned source before Ready publication. Connection-template
  validation still precedes side effects, and container ownership precedes start
  and source attachment. Nineteen focused checks pass with coverage and exact
  90-input bindings, including real queries, port identity, lost-response cleanup,
  uncertain creation and startup/disposal races. Production removes the unnecessary
  host-port probe; the container holds cleanup identity before its source exists.
  The test uses the actual competing socket and Docker inspection rather than a
  simulated networking boundary. No transient retry or deadline policy changed.
  The completed `full-gate-r4` passes243 checks without failures/skips; earlier
  failed full gates remain qualified failures.

- **Final verification — 2026-10-15:** The whole-solution gate passes243 tests
  without failures or skips in540.367 seconds. Exact90-input bindings match the
  final source. Solution and separate DynamicScaling example builds both pass
  with zero warnings/errors. Coverage executes all instrumented service-owner,
  scoped-lease and safe-diagnostic lines; logger scope delegation, default
  registration convenience and inherited platform/error paths remain explicitly
  unexecuted, with no whole-core branch or mutation claim. The new port allocation
  and both source-less/source-attached cleanup paths execute. No task containers
  remain after the full run; unrelated Docker resource0a95e88a68d8 is preserved.
  [Final candidate evidence](../../evidence/2026-10-15/di-ownership-final/manifest.json)
  retains commands, source cuts, REDs, raw results, coverage limits, prior audits
  and interrupted/failed attempts separately. Independent completion review is
  still pending; acceptance boxes and Open status remain until its exact envelope
  passes. Normal mutation awaits the whole coding-session final committed source.

## Decision log

- **2026-10-15 — Resolved:** The exact DI/static ownership candidate and finite
  administrative envelope passed independent completion review. Explicitly
  initialized scopes resolve owned leases; provider shutdown closes all returned
  handles and retains every unfinished owner/failure. The full gate, source
  bindings and final solution/separate example builds pass. The
  [completion receipt](../../evidence/2026-10-15/di-ownership-final/completion.json)
  binds the reviewed identity. TT-008, TT-010 and TT-009 remain Open; normal
  mutation awaits the whole coding-session final snapshot.

---
id: TT-001
title: Own and release public database leases
status: Resolved
priority: P0
type: Bug
created: 2026-10-14
depends_on: [TT-002]
---

# TT-001: Own and release public database leases

## Problem

`Tt.CreateContextAsync<TContext>` and `TtFactory<TContext>.CreateContextAsync` construct an internal `TtContext<TContext>` then implicitly convert it to the underlying context. Callers dispose the raw context, losing the object that owns database return. The advertised quick start therefore does not release/reset its database at context disposal. With pool size one, the runtime probe created three live containers for three sequential disposed contexts.

Source: [Tt.cs](../../../../src/TurboTest.EFCore/Tt.cs), [TtFactory.cs](../../../../src/TurboTest.EFCore/TtFactory.cs), [replacement public lease](../../../../src/TurboTest.EFCore/TtLease.cs). The former internal wrapper has been removed. Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Make ownership explicit through a public async-disposable lease/fixture contract. The illustrative `await using var lease = await fixture.LeaseAsync<MyDbContext>();` is a design direction, not a finalized signature. Decide and document how the existing raw-context APIs retain correct disposal ownership or migrate callers through an explicit compatibility policy.

## Acceptance criteria

- [x] The supported public API retains an owner until return completes; no implicit conversion can silently discard cleanup ownership.
- [x] With capacity one, three sequential leases use the same database resource and ordinary test data is absent on the next lease. A test checks identity/reuse, not just data cleanliness in a newly allocated container.
- [x] Lease disposal closes its contexts/readers/transactions before reset. Async disposal is idempotent, including concurrent disposal calls; return happens exactly once.
- [x] Context construction or schema initialization failure releases or quarantines the acquired resource without leaving an undisclosed lease.
- [x] The contract specifies synchronous context disposal, async disposal, cancellation, multiple contexts within one test, and fixture shutdown; supported paths have observable ownership tests.
- [x] Direct, factory and documented examples use the supported lifecycle. Any breaking API change has a migration example and an explicit versioning decision.

## Verification

Use fast ownership tests plus a real PostgreSQL public-API regression with capacity one. Reject a deliberate missing-return/double-return fault. TT-002 supplies the pooled-reset prerequisite; this ticket is not complete merely because fixture disposal eventually releases accumulated databases.

## Implementation log

- **2026-10-14 — internal groundwork:** Database return now retains one shared
  disposal task, so concurrent/repeated callers await the same result. Cleanup
  rejects late resource registration, closes remaining resources after a
  callback error and discards without Ready publication. Diagnostics retain
  the first cleanup failure: a later failure must not hide the original SQLSTATE
  while the remaining resources are being closed. Fast ownership checks and a
  deliberate missing-return calibration exercise this protocol without Docker.
  This does not resolve TT-001: the public raw-context APIs still discard their
  wrapper and explicit public leases/versioned migration remain to be delivered.
- **2026-10-14 — public lease implementation:** `TtLease<TContext>` retains its
  contexts until one shared asynchronous return completes. Factory shutdown
  closes outstanding leases; context construction, initialization failure and
  cancellation before publication discard their reservations. Individual
  contexts support synchronous or asynchronous disposal; additional contexts
  belong to the same lease. Public PostgreSQL regressions cover these paths,
  static owner recreation and concurrent fixtures sharing a configured prefix.
  Container names now include a unique owner identifier, avoiding a same-second
  collision between independent fixtures.
- **Breaking prerelease decision:** Source version `1.0.0-alpha.2` removes the
  raw-context APIs and implicit conversion. All maintained callers migrate to
  `CreateLeaseAsync` and `lease.Context`; README documents migration and ownership.
  There are no release tags or configured remotes, and the NuGet flat-container
  endpoint returned HTTP 404 on 14 October 2026. No released compatibility
  dependency was found. Verification/review are pending; this ticket stays Open.

- **2026-10-14 — candidate verification:** The full suite passes all 33 checks
  without skips; the solution and the separate dynamic-scaling example compile
  without warnings. Eight fast ownership checks execute the lease/return/safe
  diagnostic classes at 100% line and branch coverage. Fresh Stryker 5.0.0 rejects
  all 26 executable scoped mutants; eight catalogued tests have source-resolvable
  links, with no survivors, timeouts, uncovered mutants or unlisted links.
  Missing-return and double-return faults fail named assertions. The
  [public lease evidence](../../evidence/2026-10-14/public-leases/manifest.json)
  retains earlier failures as well as the final pass. Independent completion
  review is pending; status remains Open.

- **2026-10-14 — independent review repair:** The first review returned FAIL:
  secondary discard/deletion errors could replace the original safe diagnostic,
  and the minimal static example skipped final shutdown after exceptions. Named
  fast and PostgreSQL REDs reproduce all three compensation paths. Diagnostics
  now retain the first stage/SQLSTATE and report only safe secondary metadata;
  unconfirmed resources remain owned and unavailable. A deliberate premature
  ownership-removal fault is rejected by the retained-container regression.
  The minimal example now owns an async-disposable factory. The same injected
  runtime exception left one container before repair and zero afterward.
  The first fault-injection compile failure is retained and is not counted as RED.
  The repaired full suite passes all 38 checks without skips. Ten fast checks
  execute the ownership/diagnostic classes at 100% line and branch coverage.
  Fresh Stryker rejects all 28 executable scoped mutants with ten source-linked
  participants and no survivors/timeouts/uncovered mutants. Re-review is pending;
  both tickets remain Open. See the
  [repair evidence](../../evidence/2026-10-14/public-leases-repair/manifest.json). The existing
  stash at `0811caaa6d33c0107d31416a68b3dc30a8a218ec` remains untouched.

## Decision log

- **2026-10-14 — Resolved:** Independent completion review passed for the P0
  lease/reset candidate. All 38 full-suite checks pass without skips; solution
  and example builds pass. Ten fast ownership checks and 28 rejected executable
  scoped mutants protect the return protocol. The
  [completion receipt](../../evidence/2026-10-14/public-leases-repair/completion.json)
  identifies the reviewed candidate. Earlier in-progress entries remain as
  history. Prepared baselines, capacity bounds, recovery, DI ownership, parking,
  metrics and adaptation remain in their separate open tickets.

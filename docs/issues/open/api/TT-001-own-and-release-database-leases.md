---
id: TT-001
title: Own and release public database leases
status: Open
priority: P0
type: Bug
created: 2026-10-14
depends_on: [TT-002]
---

# TT-001: Own and release public database leases

## Problem

`Tt.CreateContextAsync<TContext>` and `TtFactory<TContext>.CreateContextAsync` construct an internal `TtContext<TContext>` then implicitly convert it to the underlying context. Callers dispose the raw context, losing the object that owns database return. The advertised quick start therefore does not release/reset its database at context disposal. With pool size one, the runtime probe created three live containers for three sequential disposed contexts.

Source: [Tt.cs](../../../../src/TurboTest.EFCore/Tt.cs), [TtFactory.cs](../../../../src/TurboTest.EFCore/TtFactory.cs), [internal wrapper](../../../../src/TurboTest.EFCore/Internal/TtContext.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Make ownership explicit through a public async-disposable lease/fixture contract. The illustrative `await using var lease = await fixture.LeaseAsync<MyDbContext>();` is a design direction, not a finalized signature. Decide and document how the existing raw-context APIs retain correct disposal ownership or migrate callers through an explicit compatibility policy.

## Acceptance criteria

- [ ] The supported public API retains an owner until return completes; no implicit conversion can silently discard cleanup ownership.
- [ ] With capacity one, three sequential leases use the same database resource and ordinary test data is absent on the next lease. A test checks identity/reuse, not just data cleanliness in a newly allocated container.
- [ ] Lease disposal closes its contexts/readers/transactions before reset. Async disposal is idempotent, including concurrent disposal calls; return happens exactly once.
- [ ] Context construction or schema initialization failure releases or quarantines the acquired resource without leaving an undisclosed lease.
- [ ] The contract specifies synchronous context disposal, async disposal, cancellation, multiple contexts within one test, and fixture shutdown; supported paths have observable ownership tests.
- [ ] Direct, factory and documented examples use the supported lifecycle. Any breaking API change has a migration example and an explicit versioning decision.

## Verification

Use fast ownership tests plus a real PostgreSQL public-API regression with capacity one. Reject a deliberate missing-return/double-return fault. TT-002 supplies the pooled-reset prerequisite; this ticket is not complete merely because fixture disposal eventually releases accumulated databases.

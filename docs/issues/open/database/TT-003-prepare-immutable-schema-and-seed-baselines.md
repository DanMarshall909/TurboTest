---
id: TT-003
title: Prepare immutable schema and seed baselines
status: Open
priority: P1
type: Feature
created: 2026-10-14
depends_on: [TT-001, TT-002]
---

# TT-003: Prepare immutable schema and seed baselines

## Problem

The existing reset builds its first template after dropping the schema, so it preserves an empty database. `EnsureCreated` later recreates tables, masking the absence of a prepared migrated/seeded baseline. Fixtures need their intended schema and nonempty seed state on every test.

Source: [TtContainer.cs](../../../../src/TurboTest.EFCore/Internal/TtContainer.cs), [Tt.cs](../../../../src/TurboTest.EFCore/Tt.cs). Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Define a fixture initializer and immutable baseline identity. Initialization can explicitly run EF migrations or `EnsureCreated`, followed by a seed callback. Do not silently substitute one schema initialization contract for the other. Capture the usable baseline before any destructive cleanup.

## Acceptance criteria

- [ ] Initialization completes schema setup and seeding once before a template or reusable database is published.
- [ ] Baseline identity includes the schema/initializer configuration needed to prevent sharing incompatible fixtures or context models.
- [ ] The immutable template is never leased or written by a workload and has no live source connections when cloned.
- [ ] Fresh prepared databases contain the expected schema, non-default seed fields, foreign-key relationships and sequence state. Real generated-ID assertions validate next values.
- [ ] Migration history and seed initialization survive recreation. A test distinguishes a migrated baseline from a database produced only by `EnsureCreated`.
- [ ] Initialization or clone failure publishes no Ready database and cleans up or retains an accounted failed reservation with a useful diagnostic.

## Verification

Use a nonempty three-table fixture and a separate migration-based fixture. The probe's seed IDs 100/200/300 and expected next IDs 101/201/301 provide one regression fixture, not a universal seed format. Define the supported seed/sequence capture contract before implementing reset plans.

## Implementation design

Separate the container host, immutable baseline and prepared database slot.
One fixture initializes a staging database once using an explicit schema mode
(`EnsureCreated`, migrations or caller initialization), then awaits its seed
callback. Freeze context options at fixture creation and include owner identity,
context type, schema mode and an explicit initializer version in baseline
identity. Per-lease configuration cannot change the prepared model.

Dispose initialization contexts and their source before sealing the template
with `ALLOW_CONNECTIONS false`. Workloads receive cloned slots, never the
template source. Clone and validate before publication. Whole-template
recreation provides the first correct return path; TT-004 adds the row path.
PostgreSQL cloning requires a connection-free source and does not copy
database-level permissions/settings; support for those must be explicit.
See [PostgreSQL database cloning](https://www.postgresql.org/docs/16/sql-createdatabase.html).

Capture every permanent user sequence's qualified name, definition, `last_value`
and `is_called`, including standalone and unused sequences. Do not infer state
from primary-key maxima. Three-argument `setval` preserves unused/custom-start
state, but its changes survive rollback. A sequence with `CACHE > 1` requires
slot-source drainage or recreation because pooled sessions can retain allocated
values. See [sequence functions](https://www.postgresql.org/docs/16/functions-sequence.html)
and [sequence caching](https://www.postgresql.org/docs/16/sql-createsequence.html).

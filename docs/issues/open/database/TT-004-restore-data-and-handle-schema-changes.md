---
id: TT-004
title: Restore data and handle schema changes
status: Open
priority: P1
type: Feature
created: 2026-10-14
depends_on: [TT-003]
---

# TT-004: Restore data and handle schema changes

## Problem

Prepared databases need a fast data reset that restores the baseline, including seed rows changed/deleted by tests. The prototype's child-first DELETE plus reseeding was faster than TRUNCATE on the measured small/medium fixture. Schema-changing workloads need a separate recreation path.

Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md). Observed sequential reset-to-verified-ready DELETE p95 was 17.67/18.57/64.19 ms at 130/1,300/13,000 inserted rows. These are workload observations, not defaults or guarantees.

## Scope

Build/cache a foreign-key-aware cleanup plan for each baseline, restore supported seed/sequence state, and use baseline recreation when row cleanup cannot safely satisfy the contract. Choose/document supported schema-change detection or explicit schema-changing mode; the prototype's public-schema fingerprint is incomplete.

## Acceptance criteria

- [ ] Updating/deleting seed rows, inserting parent/child rows and advancing sequences is followed by exact baseline restoration, including next generated IDs and relationships.
- [ ] The cached plan handles supported foreign-key ordering; cycles, triggers and unsupported objects produce a documented safe strategy or explicit fallback, never silent partial cleanup.
- [ ] A changed schema cannot pass as an ordinary clean database. At minimum, tests adding a table/column demonstrate baseline recreation before reuse.
- [ ] The supported catalog scope is explicit, including the behavior for views, routines, types, grants, extensions and non-public schemas.
- [ ] Reset success is reported only after required baseline validation. Errors, including a failure after `setval`, cannot publish a dirty database; sequence changes cannot be assumed to roll back with row changes.
- [ ] Performance measurements include restoration and verification, and account for executing cleanup plus time waiting for a reset permit.

## Verification

Reject faults in row cleanup, reseeding, sequence restoration and schema detection. Keep real PostgreSQL checks for sequence rollback behavior and schema recreation. Do not hardcode the three-table experiment's SQL as a general reset implementation.

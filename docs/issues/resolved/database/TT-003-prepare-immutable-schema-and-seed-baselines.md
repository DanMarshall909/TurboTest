---
id: TT-003
title: Prepare immutable schema and seed baselines
status: Resolved
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

- [x] Initialization completes schema setup and seeding once before a template or reusable database is published.
- [x] Baseline identity includes the schema/initializer configuration needed to prevent sharing incompatible fixtures or context models.
- [x] The immutable template is never leased or written by a workload and has no live source connections when cloned.
- [x] Fresh prepared databases contain the expected schema, non-default seed fields, foreign-key relationships and sequence state. Real generated-ID assertions validate next values.
- [x] Migration history and seed initialization survive recreation. A test distinguishes a migrated baseline from a database produced only by `EnsureCreated`.
- [x] Initialization or clone failure publishes no Ready database and cleans up or retains an accounted failed reservation with a useful diagnostic.

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

## Implementation progress

- **2026-10-14 — first RED:** P0 prerequisites are independently accepted and
  committed at `0fe83327c02602edb45141f83a55c0a1b3b2df45`. A public fixture
  regression specifies one initializer, three non-default seeded tables,
  relationships and generated IDs 101/201/301 across two leases. The compiling
  baseline-definition/API scaffold currently delegates to the old path and
  ignores initialization. The regression fails on expected calls 1 versus actual
  0; this is missing behavior, not setup failure. The
  [RED evidence](../../evidence/2026-10-14/seed-baseline-red/manifest.json)
  retains the source and result. No GREEN or baseline completion is claimed.
- The next implementation uses one fixture host, a private administrator source
  and a separate workload role. The administrator owns/seals the template;
  workloads receive owned clones and cannot reopen or alter the template.
  Freeze model configuration once, close initialization sources before cloning,
  and verify clones before publication. Whole-template recreation precedes the
  cached row-reset plan under TT-004. This concentrates the template/credential
  boundary rather than exposing administrator access through ordinary contexts.

- **2026-10-14 — first GREEN and coverage:** The three-table public regression
  passes across two leases. One fixture host initializes a template once, drains
  its initialization source, transfers database ownership to a private admin and
  seals connections before cloning workload-owned slots. Return currently
  recreates a slot from the template. The [focused evidence](../../evidence/2026-10-14/seed-baseline-green/manifest.json)
  retains the successful run and coverage diagnostic. Production refactoring
  separates the two pool contracts; the test's independent non-default and
  generated-ID oracles need no structural change yet. Failure, immutable-model,
  migrated-baseline and clone-content checks remain unproved. No completion is
  claimed; `SELECT 1` alone is only a connection check, not baseline validation.

## Initial capture contract

The prepared-baseline path supports permanent ordinary user tables, namespaces,
columns/defaults/identity/generated values, ordinary indexes and constraints,
and every permanent user sequence (including standalone/unused sequences).
Seed-content validation streams sorted SHA-256 hashes of PostgreSQL binary
records and preserves duplicate multiplicity. It retains per-object hashes, not
seed payloads, with a 10,000-entry catalog limit and a 10,000-relation limit.
This is hash-based equality of clones on the same host/version, not a portable
serialization or a claim of mathematical collision-free equality.

Until corresponding catalog definitions are captured, views, routines, user
triggers/rules, RLS, custom types/extensions, partition/inheritance structures,
foreign/unlogged relations are rejected with `BaselineUnsupportedCatalog`.
Temporary objects are session-local and are outside the permanent baseline.
Custom database ACLs, connection limits and database/role settings are also
rejected because PostgreSQL does not clone them. This explicit support boundary
avoids accepting a partial schema fingerprint as complete validation. TT-004
can extend supported reset/catalog plans with their own acceptance evidence.

Baseline identity binds the fixture's unique owner, context type, schema mode,
explicit initializer version and frozen EF options/model. Different fixture
owners never share templates. Per-lease configuration is rejected before
reservation, and the initializer must target its fixture-owned workload source.

## Current verification and next repairs

- **2026-10-14 — working baseline path, not completion:** The full suite
  passes **64 tests**, with no failures or skips; the post-refactor full run also
  passes 64. Restored catalog/fast checks pass 16. The baseline path preserves
  non-default seeds, relationships, real generated IDs, migration-only schema
  and EF history across recreation, and unused/advanced standalone sequences
  with `CACHE 5`. Permission tests observe a connection-free sealed template
  owned by the private administrator, and workload roles lack administrator
  privileges. Actual source-connection contention rejects cloning with SQLSTATE
  `55006` and removes the failed factory's host.
- Ten fast manifest/configuration checks pass. A removed validation gate is
  rejected by four independent manifest-difference oracles; replacing migrations
  with `EnsureCreated` fails the migration-only fixture with SQLSTATE `42P01`.
  These are scoped calibration/verification results, not whole-package mutation
  proof. Normal mutation remains pending the final committed source snapshot.
- The deliberate unsupported-catalog RED unexpectedly returned six factories,
  which the original `Assert.ThrowsAsync` harness left open. Exact container IDs
  were correlated to the six RED test intervals and their private admin role,
  then removed. The refactored negative-factory helper observes publication
  independently and disposes unexpected factories in `finally`. Replaying a
  removed catalog guard fails all six publication assertions and leaves no
  TurboTest containers. The final restored checks also leave none; the unrelated
  container is retained.
- [Progress evidence](../../evidence/2026-10-14/prepared-baseline-progress/manifest.json)
  retains raw results, coverage, calibration/restoration hashes, cleanup receipt
  and current source snapshots. Historical failed attempts remain labelled.
  Source is uncommitted and TT-003 remains **Open**.

A bounded read-only investigation identified these concrete repairs before
completion review (this is an investigation, not a completion verdict):

1. Failed startup cleanup currently throws a diagnostic containing only strings
   and loses the only reachable failed owner. Retain an opaque cleanup capability
   with safe resource accounting, and support an explicit retry after terminal
   cleanup failure while admission remains closed. Verify initializer/clone
   failure plus failed host deletion, then successful cleanup of that exact host.
2. Administrator snapshot verification must be followed by a workload-source
   identity/query probe. An initializer may change its own password or global
   role defaults; no unusable workload source may become Ready. Reject unsupported
   global role defaults as well as database-specific settings.
3. Reject or capture user collations, dormant policies, extended-statistics
   definitions and text-search objects. Capture sequence `OWNED BY` dependency
   edges: definition/last-value alone cannot detect `OWNED BY NONE`. Add real
   corruption/rejection oracles for these omitted permanent-state categories.
4. Reject unsupported allocation/scaling/connection-template settings before
   creating resources; validate provider and positive prepared-slot count.
   Document generated names, slot-count semantics and explicit schema-mode
   precedence. Prepare the configured Docker image rather than always pulling
   the default image.

These are TT-003 acceptance gaps. They are not deferred merely because TT-006
will later add broader recovery policy. Independent completion review and the
completion commit remain pending after these repairs.

- **2026-10-14 — failed-startup ownership repair:** Four real PostgreSQL/Docker
  RED cases confirmed that initializer/clone failure plus failed host deletion
  returned no cleanup capability. They now pass: the exception retains an
  opaque `TtCleanup` owner/resource snapshot, explicit retry keeps admission
  closed, and the process registry retains the owner even if the caller drops
  the exception. `Tt.DisposeAsync` also drains these failed startups. Three fast
  checks prove shared concurrent attempts, stable accounting, safe errors and
  cleanup capability preservation through secondary failures; `TtCleanup` and
  its async body execute at 100% line/branch coverage. The combined 17 ownership
  checks pass. Registry aggregation failure still needs fast coverage. Tests
  using process-wide lifecycle state share an xUnit collection.
- **2026-10-14 — workload usability repair:** Two RED cases returned unusable
  factories after a seed changed its own password or global role defaults.
  A final workload-source identity/query probe now rejects stale credentials
  with `BaselineWorkloadVerify` / SQLSTATE `28P01`; unsupported global role
  defaults fail capture. Those two cases and the successful three-table fixture
  pass. This follows the ordinary-role permissions in
  [PostgreSQL ALTER ROLE](https://www.postgresql.org/docs/16/sql-alterrole.html).
  Catalog/dependency and option-validation repairs remain in progress.

## Repaired candidate verification

- **2026-10-14 — candidate for independent review:** All four investigation
  findings above are repaired. Startup failures retain a retryable opaque owner
  in both the exception and process registry; explicit/global cleanup retries
  delete the exact retained host. Global cleanup continues through other owners
  after one fails. Four PostgreSQL failure/retry cases and four fast cleanup laws
  assert those outcomes. The scoped cleanup class executes at 100% line/branch;
  the registry's duplicate-owner invariant branch remains uncovered.
- Workload verification rejects changed credentials and unsupported role defaults.
  Capture rejects collations, dormant policies, extended statistics and
  text-search objects, and includes sequence ownership edges and catalog comments.
  Real clone corruption now covers row content, sequence state/ownership,
  constraints and comments; unsupported-catalog cases use independent publication
  assertions and dispose unexpected factories.
- Prepared options are validated before image/resource allocation. The configured
  cached image is prepared and actually used by the Docker host. Boundary tests
  cover generated-name UTF-8 limits, safely quoted credentials and immutable
  option snapshots. Explicit schema mode wins over legacy `AutoMigrate`.
- Primary and additional contexts must retain the fixture's exact source and
  frozen model. Source/model substitution is rejected before a lease escapes.
  Two fixtures with the same context/version but different models stay isolated.
  `EnsureCreated`/migration initialization rejects inconsistent runtime versus
  schema table/column mappings; caller-owned schema remains an explicit mode.
- The final full suite passes **108 tests**, with **zero failures/skips**.
  Solution and separate DynamicScalingExample builds pass with zero warnings or
  errors. The run leaves no TurboTest containers or temporary image alias; the
  unrelated container is preserved. Full coverage is retained without claiming
  whole-package 100% coverage. Defensive catalog-size guards and some cleanup
  compensation paths remain coverage gaps, separately visible in the report.
- [Repair evidence](../../evidence/2026-10-14/prepared-baseline-repair/manifest.json)
  retains raw RED/GREEN results, full coverage, source snapshots and failed setup
  attempts. The wrong solution suffix and image-tag property were build/setup
  errors, not behavior REDs. A first same-version model-isolation setup used a
  runtime model inconsistent with EF's schema model; that failed setup is retained,
  and the corrected independent isolation oracle passes.
- Refactoring separates validation, catalog capture, prepared ownership and
  failed-owner cleanup. Fast configuration/manifest laws complement actual
  PostgreSQL capture and lifecycle checks. The existing removed-validation and
  migration-substitution calibrations remain scoped historical evidence; normal
  mutation is pending the final committed snapshot and will exclude end-to-end
  tests. No fresh mutation result is claimed.
- TT-004 still owns the row-reset path, TT-005 capacity limits, and TT-006 broader
  runtime recovery/drain policy. TT-003 remains **Open** until independent review
  passes and its exact completion envelope is committed.

## Decision log

- **2026-10-14 — Resolved:** Independent completion review passed for immutable
  prepared baselines. All 108 full-suite tests pass with no failures or skips;
  solution and separate example builds pass. The
  [completion receipt](../../evidence/2026-10-14/prepared-baseline-repair/completion.json)
  identifies the exact candidate. Earlier progress remains historical. Row reset,
  bounded capacity, recovery, DI, parking, metrics and adaptation remain Open.

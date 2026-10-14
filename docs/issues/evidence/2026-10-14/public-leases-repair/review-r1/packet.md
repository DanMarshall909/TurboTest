# P0 candidate: public lease ownership and pooled reset

Review TT-001 and TT-002 for completion. The broader active goal implements all
10 tickets from docs/issues/README.md; this candidate does not complete it.
Read AGENTS.md and rule-sets/ai-rules.md. Use the agentic-delivery completion-review
reference. All changed paths are task-owned and in scope; no unrelated paths are
excluded. The repository has one master worktree, no other refs, no remote or
upstream. No integration operation is available or required by its local-only
tracker. Base/current/integration object identities are in manifest.json.

The candidate is staged. manifest.json records every changed path in bytewise
order, status, mode, SHA-256 content/deletion marker and staged Git object.
manifest.sha256 identifies its serialized bytes. Independently reconstruct the
manifest and verify its integration identity before assessing the candidate.
No repository edits or delegated approval decisions are authorized for review.
Do not run builds/tests that modify this shared candidate; retained raw results
are available. Return exactly PASS, FAIL or BLOCKED with criterion-to-test and
task-to-implementation matrices and actionable findings as appropriate.

## Scope and evidence

Authoritative criteria: docs/issues/open/api/TT-001-own-and-release-database-leases.md
and docs/issues/open/database/TT-002-reset-with-pooled-connections.md.
Partial related work: TT-006 startup ownership/readiness fixes; TT-010 removal of
unsupported claims; TT-003 baseline design only. All these other tickets remain
Open. DI still has the known TT-007 owner defect, allocator/recovery/controller
work remains under TT-005/006/008/009, baseline initialization/restoration under
TT-003/004, and integrated metrics/benchmarks under TT-010.

Current raw evidence and commands are in
 docs/issues/evidence/2026-10-14/public-leases/manifest.json.
33 tests pass, zero failures/skips (full-reviewed.trx), solution build passes,
and DynamicScalingExample outside the solution has a separate passing build.
Full test command runs from repository root. Stryker command runs from
 tests/TurboTest.Tests; 26 executable scoped mutants are Killed, 15 Ignored and
one CompileError. Eight catalogued source-resolvable participants, no unlisted
links/survivors/timeouts/NoCoverage. End-to-end tests are excluded. Fast ownership
classes execute at 100% line/branch coverage; other code is not claimed at 100%.
Deliberate public missing/double-return faults fail named assertions; receipts
verify fault application and restoration. Earlier reset/internal mutation evidence
is retained under pooled-reset and ownership-protocol with snapshot limitations.

Failures are retained: initial orphaned-run cleanup, same-prefix container name
collision, constructor reservation leak, cancellation publication, raw-context
ownership loss and readiness post-open query fault; some baseline runs also failed
on slow initdb. The final startup correction uses initdb --no-sync only for
initial disposable-cluster creation, with server fsync=on checked against real
PostgreSQL. README records crash/recreation behavior. No latency guarantee is
inferred. Invalid compile/setup runs are retained and not called valid RED.

## Implementer's criterion evidence map (independently assess)

| Criterion | Implementation | Executable evidence |
|---|---|---|
| TT-001 AC1 owner retained/no implicit conversion | TtLease, TtFactory; raw APIs/wrappers removed | LeaseOwnershipTests; public reuse regression; source/API inspection |
| TT-001 AC2 three sequential capacity-one identity/data reuse | owned data source + lease return | Three_sequential_public_leases_return_and_reuse_one_database |
| TT-001 AC3 resource order/idempotent concurrent return | registered contexts + shared completion | Public_lease_closes_all_contexts_and_waits_for_one_return; Failed_public_lease_return_is_shared_without_retrying_cleanup; Multiple_contexts_and_sync_disposal_keep_the_lease_owner_until_reader_cleanup; DatabaseOwnershipTests |
| TT-001 AC4 construction/init failure no undisclosed lease | acquisition catch + DiscardAsync + ownership ledger | Constructor_failure_does_not_leave_an_undisclosed_database_lease; Schema_initialization_failure_is_reported_before_a_lease_is_published; Abandoned_reservation_closes_resources_and_discards_without_reset_publication |
| TT-001 AC5 documented sync/async/cancel/multiple/shutdown | TtLease/CreateContext, fixture closure, README | cancellation, multiple-context and fixture-shutdown PublicLeaseTests; failed/repeat core laws |
| TT-001 AC6 direct/factory/examples/migration/version | static owner, factory, all examples, alpha.2, source installation | static owner recreation and independent fixtures PublicLeaseTests; solution and separate example builds; NuGet 404/tags inventory |
| TT-002 AC1 pooled repeated reset/no replacement/55006 | transactional ordinary row reset | Repeated_factory_returns_reuse_the_database_and_its_warm_session; three public leases |
| TT-002 AC2 drain reader/transaction before exclusivity | context callbacks before row reset; ordinary reset no clone/drop | Return_closes_an_outstanding_reader_and_transaction_before_reset; multi-context public check; callback laws |
| TT-002 AC3 fixture source isolation/no ClearAllPools | fixture-owned sources/container IDs | Reset_and_disposal_leave_another_fixture_session_and_rows_untouched; same-prefix independent fixtures |
| TT-002 AC4 staged failure/unavailable/no secrets | TtLifecycleException + quarantine by discard | Failed_reset_reports_its_stage_and_does_not_publish_the_dirty_database; Failed_lease_cleanup_is_reported_and_cannot_return_a_resource_to_the_pool; Locked_rows_cause_a_bounded_reset_failure_and_are_never_reused |
| TT-002 AC5 identity and ordinary rows checked on PostgreSQL | ordinary TRUNCATE plan | repeated identity/public reuse, pgfixture schema regression |

## Proposed post-PASS envelope

Exact operations and resulting content hashes are in envelope.json, with proposed
bytes under post/. It resolves/moves only TT-001 and TT-002, updates their index
links/status, and writes an exact completion receipt identifying this manifest.
No semantic source/test changes are allowed. Apply only after a PASS, verify the
candidate identity again, compare the full final tree with these transformations,
validate archive bytes and Markdown links, inspect the staged diff and commit
locally with normal hooks. No remote, push, worktree or generated-output cleanup
is proposed. Overall goal remains active for the eight open tickets.

FAIL

Independent reviewer: /root/p0_completion_review.
Candidate manifest: 6bb2d1739307508788dd291417a0ba2d535ac92d4d9100ec1bdd7445f86b900e.
204 staged paths and 164 archived digests independently reconstructed/verified.
Base/current/integration: 5de941b6d729f62f83575f65f1420ae0262e1235 on master; no remote/upstream.
33 checks pass, builds have no warnings/errors, scoped mutation 26 Killed,
15 Ignored, one CompileError, eight source-resolvable participants, no unlisted links.

Finding 1: TtDatabase.DisposeCoreAsync, TtPool.ReleaseAsync and TtFactory.CreateLeaseAsync
allow a secondary discard/delete exception to replace the original safe lifecycle
stage and SQLSTATE. This violates TT-002 AC4 and documented first-failure behavior.
Preserve original stage/SQLSTATE, safely report secondary cleanup failure, retain
unconfirmed resources in the ownership ledger and never publish them for reuse.
Focused evidence required: cleanup followed by failed discard, reset followed by
failed deletion, initialization followed by failed discard. Recovery/retry remains TT-006.

Finding 2: MinimalExample calls Tt.DisposeAsync only after successful lease block
completion. Acquisition/query/return exceptions skip shutdown and leave static
containers (AutoRemove=false). This violates TT-001 AC6. Use await-using factory
or a complete static try/finally, and correct the context-disposal cleanup comment.

Inventory correction: refs/stash exists at
0811caaa6d33c0107d31416a68b3dc30a8a218ec. No extra branches/worktrees or integration
blocker exists. Preserve the stash and replace the literal no-other-refs claim.

Task matrix: TT-001 main implementation complete except example/secondary failures;
TT-002 reuse/isolation complete except secondary-failure diagnostics;
TT-003 design only; TT-006 and TT-010 partial work accurately kept Open.
Criterion matrix: TT-001 AC1/AC2 satisfied; AC3/AC4/AC5 supported normal-discard paths
but need secondary failure protection; AC6 minimal exception path incomplete.
TT-002 AC1/AC2/AC3/AC5 satisfied; AC4 incomplete on failed compensation.

Alternatives: existing explicit lease/shared completion task is appropriate;
use deterministic factory ownership in the minimal example and consistent safe
failure preservation around compensating cleanup.
Lesson candidate: test compensating cleanup failure after an original lifecycle
failure. Narrow owner is repository ownership tests; no standing guidance change.

Repair capsule: preserve this snapshot; add named REDs for three compensation
paths and example exception exit; implement safe diagnostics while retaining the
existing owned-resource ledger; rerun affected checks, full gate, core coverage
and fresh mutation; rebuild the whole packet and return to the same reviewer.
Overall ten-ticket goal remains active. No completion envelope may be applied yet.

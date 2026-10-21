# Cleanup ownership slice — TT-024 / TT-023

This advances the cleanup ownership criteria coordinated by
[TT-024](https://github.com/DanMarshall909/TurboTest/issues/14) and
[TT-023](https://github.com/DanMarshall909/TurboTest/issues/13). Both issues remain
open. This slice does not settle all retained-state, registration, cancellation,
provider or ledger questions and does not archive either investigation.

## Observable contracts and evidence

| Contract | Owning assertion and evidence |
| --- | --- |
| Global retained cleanup exposes every failed owner, independent of registration order | `Global_cleanup_reports_each_failed_owner_and_returns_a_capability_that_retries_them_all`: two RED rows miss the other safe stage; GREEN rows retain two independent resources and retry both callbacks to completion. `multiple-cycle.json`. |
| Explicit, causal and secondary handles preserve every distinct remaining operation | Two-row precedence theory covers explicit handle present/absent, exact resource union, duplicate supplementation identity, and no passive-group registry entry. RED returns only explicit/causal resource; GREEN performs each expected operation once. `precedence-cycle.json`. |
| Known startup cancellation, drain and retry failures remain visible together | New startup recovery scenario first rejects lost nested `RetryHostDelete` stage. With stage preservation alone it rejects missing `StartupCancellation`. GREEN asserts original stage, `53100`, distinct exception kinds, unchanged recovery capability, retained reservation after failure and successful third physical deletion. `startup-failures-cycle.json`. |
| Independently failed static owners both remain recoverable | Real PostgreSQL test creates and lists two distinct Docker IDs, rejects missing `StaticSecondDelete`, then proves the returned capability deletes both exact resources once, followed by empty Docker inventory. `static-multiple-cycle.json`. |
| Completed child ownership does not displace remaining recovery | RED returns an unnecessary new handle for a completed child plus active owner. GREEN keeps the remaining owner's handle, does not recall the completed child, and completes recovery. `completed-child-cycle.json`. |
| Composed recovery shares pending/reentrant work and retries only failed children | New characterization begins GREEN. Nested/duplicate causes retain two operations even when their resource labels match; gated concurrent/reentrant calls share the pending task; one child stays completed and the failed child alone retries. Final composite failure handler coverage is executed. `composition-retry-cycle.json`. |

The existing native throwing-callback scenario also asserts the cancellation
and deletion diagnostics in its first failed recovery response, then successful
recovery after deletion is enabled. It no longer requires an additional error
that has already been delivered.

All raw commands, source identities, TRX files, coverage and phase receipts are in
`runs.tar.gz`, with every member's digest in `manifest.json`. Setup failures
(missing solution path, missing namespace and nullable test compilation) remain
separate from the subsequent named behavioral REDs. They are not assertion evidence.

## Ownership decision

The existing single `Cleanup` capability remains the public recovery surface.
Explicit, causal, secondary and nested aggregate handles are combined by object
identity, preserving every remaining operation. Resource labels are only a view;
equal labels do not merge owners. One remaining owner preserves its handle.
An already supplied passive group covering the same remaining set is reused.
Only passive forwarding groups flatten: real provider/factory wrappers may own
additional diagnostic obligations and retain their own operations.

Passive groups create no global registry entry. Each child already shares its
pending attempt and caches success. A completed callback contributes no remaining
work; an empty physical-resource list alone cannot establish completion because
diagnostic-only recovery can remain. Successful groups clear callback, resource
and member references. Multi-failure drain preserves a single failure's identity
and safely aggregates multiple failures with all remaining capabilities.

Safe diagnostics preserve nested lifecycle stages and SQLSTATE strings while
omitting arbitrary exception messages and raw inner exceptions. Multiple SQLSTATE
values remain in the safe aggregate diagnostic; no arbitrary aggregate member is
promoted to the single `SqlState` property.

## Refactor and qualification

Production and test decisions are recorded separately in each cycle receipt.
The shared failure emitter was extracted only after registry and static drain
needed the same behavior. No new public collection or registry owner was added.
The real two-owner case stays in the slow lane because it proves independently
listed physical identities and deletion. Fast ownership laws cover permutations,
composition and partial retries without Docker. Its repeated native timings vary
with two-container startup/deletion; no lane movement is inferred from one run.

Owning coverage executes the changed behavior; it is not a whole-issue coverage
or mutation adequacy claim. Mutation remains queued after the whole coding
session against the final immutable source. TT-024 still owns CTS/registration
and completed-work lifetime evidence, dominated fallback/internal-domain decisions,
additional reentrant drain cases and exact disposition of all 48 linked questions.
TT-023 still owns remaining ordering, timeout/cancellation, monitor and provider
cases. The current source deliberately retains those behaviors for their owning work.

Source and evidence are local, according to repository policy. GitHub remains
the authority for issue status and priority. No hosted CI or code push is claimed.

## First full gate and repair

The first immutable-source ordinary gate completed in816.625s with275 fast
passes and166/168 native passes. Both named failures, full raw TRX, and empty
before/after container inventories remain in `ownership-final`. No passing final
gate is inferred from this run. The native throwing-callback test expected a
second cancellation error after the new collector had already delivered it in
the first failed retry. The repaired test requires both `SlotDispose` and
`StartupCancellation`, preserves the same remaining capability and resource ID,
requires exactly one cancellation callback, and then requires successful cleanup.

The other failure was `PreparedAdaptiveTests.Growth_canceled_while_waiting_for_an_administrator_is_cooperative_shutdown`: its shared10s test token canceled
observation at drain WaitAsync. Two isolated reruns on identical source and
binaries both passed (6s and8s reported test body). These controls do not prove
a root cause or absence of a race. Its source, deadline and ordinary-lane
selection are unchanged; the subsequent complete gate supplies fresh cohort
evidence. No production change was made between these ordinary gate attempts.

## Second full gate and native diagnosis

The second full ordinary gate (`ownership-final-r2`) completed in714.452s:
275 fast passes and166/168 native passes. Its failed rows were the public
one-to-eight adaptive scenario (`DrainTimeout`) and a subsequent global startup
cleanup scenario (`PreparedPoolDispose` with a safe Npgsql failure). The retained
TRX does not identify the underlying server wait, or prove that both failures
share the same owner. Before/after labelled inventories were empty and all
source/compiler inputs remained unchanged. Neither failed run is passing evidence.

A separate, source-bound diagnostic assembly preserves the public adaptive
scenario's original assertions and deadlines, adds safe phase records, and
explicitly retries retained cleanup in its finalizer. In `cohort-1`, all body
assertions passed before teardown. The first actual Delete ran for30.056s and
failed; teardown later reported its safe Npgsql failure. Explicit recovery
released the remaining slot and host, retained the unseen diagnostic, and a
further observation completed. Thus this reproduction locates a native Delete
stall, rather than an assertion failure hidden by final disposal. It does not
identify the PostgreSQL wait or establish a cause for the separate growth test.

PostgreSQL16.15's pinned `dropdb` source includes synchronous checkpoint and
all-process SMGR-release barrier waits before directory removal. Those are
possible distinguishable stages, not an inferred root cause. The one-off
observer samples safe activity fields, operation categories, blocking PIDs and
fixed-format barrier messages; it retains no arbitrary SQL, credentials or raw
exception messages. Its external diagnostic connections are disclosed and do
not qualify performance or managed administrator-budget claims.

Diagnostic controls2–5 and7–8 passed. Control6 has no terminal result: its
process handle is absent, its phase trace stops during teardown, and its exact
identified container was found stopped and removed after recording its state
and reservation label. It is recorded as interrupted, never as PASS. A second
stopped labelled container has uncertain ownership and remains untouched. The
fresh ordinary gate records it as a baseline resource; settlement requires an
unchanged inventory rather than claiming an empty machine. The lost temporary
diagnostic build was rebuilt in an ignored reusable directory; primary source,
dependency and compiled input hashes still exactly matched the second gate.

No production timing, deadline, concurrency, lane selection or PostgreSQL setting
was changed to obtain a passing result. The earlier native delays remain an
explicit investigation limitation unless further server evidence identifies
their cause. Both TT-024 and TT-023 retain their unfulfilled broader criteria.

During the fresh r3 full run, the read-only sampler directly observed three
DROP backends in `IPC/CheckpointDone` with their simultaneous checkpointers in
`IO/DataFileSync`, and no blocking PIDs in those samples. Their query ages were
approximately3.5–6.1s. This is evidence of the observed checkpoint/storage wait
in r3, not proof that the earlier30s timeout had the same cause. The native
adaptive cases both passed in r3 at this observation point. Terminal catalog,
source and resource reconciliation are still required for a passing full gate.

The exact48 original TT024 questions are reconstructed with original source,
location, replacement, report identity and historical status in
`question-source-inventory.json`. A working evidence map identifies the three
cleanup-precedence questions addressed by this slice and leaves45 unresolved.
Separate source-only analysis of466 and723 is explicitly unexecuted and removes
no useful code. It identifies a viable foreign-provider diagnostic distinction
and duplicate zero-capacity enforcement with unspecified multi-invalid precedence.
Neither investigation is represented as an executed mutant result or ticket closure.

The r3 ordinary gate terminated PASS in596.839s, with exact discovery/execution
of275 fast and168 PostgreSQL rows (443 total), no failures or skips, unchanged
compiler/test/dependency inputs, and unchanged labelled container inventory.
The pre-existing stopped container remains preserved. Its full ordinary receipt,
raw TRX and safe server observations qualify this source cut; they do not erase
the two historical failures, prove their root cause, or settle all TT024/TT023
requirements. The archive excludes the reusable diagnostic build cache while
retaining its build log and source/compiled-input identities.

Final ordinary gate: **275 fast + 168 PostgreSQL = 443 passed**, no failures/skips. Total 596.839s. Labelled Docker inventories before and after match exactly; the pre-existing stopped container of uncertain ownership was preserved. Exact discovered/executed ID partition is retained. Final 20-row owning coverage passes against the same source. Independent verdict is recorded separately in `completion.json` after review.

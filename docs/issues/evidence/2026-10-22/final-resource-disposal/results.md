# Final-resource disposal contracts

## Scope and bounded outcomes

This TT024 slice characterizes existing final disposal at the ordinary adapter
and provider shutdown source. Production remains unchanged. A maintained
two-row adapter test covers never-started and failed-image-start owners:
successful closure rejects later initialization before image preparation can
run, repeated closure succeeds, acquisitions reject closure and no resource
remains owned. The existing real Docker/PostgreSQL gate protects actual
allocation, deletion, failed cleanup and successful retries.

Original question 661's omitted semaphore disposal fails both new cases by
allowing image preparation to run after closure. Three independent preparation
and recovery controls still pass. The baseline is characterization, not a
newly reproduced production defect or product-repair RED.

Original questions 660 and 2218 have separate one-off diagnostics. Private
reflection selects the actual owned client/source; it adds no public API and
does not enter maintained tests. A real Docker SDK version request succeeds
before closure, and the owned client rejects requests after successful pool
drain. The provider source's diagnostically requested kernel handle remains
open while its successful cancellation callback is held, then closes by
successful actual drain completion. Normal operation is not claimed to
allocate that handle. `CancelAsync` precedes the source-disposal `finally`, so
this evidence does not establish release on every cancellation-failure path.
Repeated provider closure joins the same job. Omitting each disposal rejects
only its owning diagnostic, with three controls passing. Test cleanup explicitly
disposes the selected resources even in fault runs.

These three decisions retain useful code, bringing the original investigation
map to 33 bounded decisions and 15 unresolved questions. The ordinary disposed
guard/flag pair, optional callback/ledger invariants and other remaining domains
are not resolved by this evidence. No acceptance checkbox or issue closure is
claimed; GitHub remains authoritative for the live checklist and status.

## Evidence quality and correction

The first SDK diagnostic expected a buffered version request to leave a live
socket. Its precondition failed before disposal: no additional socket existed.
The exact original source, binding, log and TRX remain in `probe-control/`, and
`probe-correction.json` labels this as setup/oracle correction, not product RED.
The corrected diagnostic observes SDK disposed-object behavior. It does not
claim a persistent native socket leak in the public pool domain.

The diagnostic source is compiled through explicit substitutions and an added
one-off test input into an isolated reusable build. Actual Compile inventories
exclude the original source for each fault and exclude prior private probes.
Source, primary binaries and dependency identities remain unchanged throughout.
Each command retains output, exit status, timing and stopped-worker receipts.
Native Docker inventory is identical before and after every diagnostic; no
containers are created by these version-request checks.

Maintained owning coverage executes both adapter cases and all final-disposal
sequence points against the final source and runtime binding. Coverage proves
execution; the exact omitted-disposal fault proves the new assertions detect
the boundary violation. Unrelated successful/native initialization is outside
that two-row coverage selection. New cases remain fast, and the canonical
fast lane passes all 308 checks before the next criterion.

## Design and reflection

No production refactor is required: the three disposals have demonstrated
admission or final-resource roles. One small theory covers two distinct startup
histories without duplicated native setup. Private ownership selection stays
in one-off diagnostics rather than coupling maintained tests to field names.

The socket precondition correction establishes a local evidence limit: a
missing disposal statement alone does not demonstrate an operating-system leak
when the actual transport is already settled. The revised result claims only
what its SDK and kernel-handle oracles observe. No standing guidance or memory
was changed.

Whole-session mutation remains pending on the final coding-session snapshot.
Bounded source-fault diagnostics do not alter original mutation statuses or
establish a fresh adequacy score. Source commits remain local; no code remote
or hosted CI result is claimed. Caches, evidence and unknown baseline resources
are retained. Final independent verdict belongs in `completion.json` only
after review of the frozen candidate and exact finite envelope.

## Final ordinary gate

**308 fast + 170 PostgreSQL = 478 passed**, no failures/skips. Total 587.111s. Solution and separate example builds pass. Exact discovered/executed IDs, source/binary/dependency identity, stopped owned process groups and matching before/after labelled Docker inventories are retained. The final 2-row owning coverage binds identical inputs. Independent final verdict is recorded separately in `completion.json`.

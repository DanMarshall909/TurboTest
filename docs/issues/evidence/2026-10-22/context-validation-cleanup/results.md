# Context validation and cleanup ownership

## Scope

This bounded TT024 slice protects two consumer boundaries and removes duplicate
cleanup state whose role is already owned elsewhere. It adds two maintained
tests and preserves the existing public API, resource limits, retry counts,
lanes, concurrency, timeouts and PostgreSQL configuration. GitHub owns the live
checklist and status; the issue remains open.

| Original question | Outcome and evidence |
| --- | --- |
| 723 | Keep zero-capacity validation. A public adaptive factory must report `PoolSize`, before its default minimum becomes invalid as a consequence. The exact boundary fault fails only the new structured diagnostic assertion; 25 configuration controls pass. |
| 1961 | Keep cancellation after context registration/options validation. Cancellation from real `OnConfiguring` suppresses optional `EnsureCreatedAsync`; the unpublished context closes and its source is discarded once. The exact omitted check fails only the new named schema-operation assertion; three binding controls pass. |
| 658/662 | Remove the ordinary completed-disposal flag, guard and assignment together. Successful ledger cleanup is cached; the concrete owned SDK and semaphore tolerate later disposal. Failed cleanup remains retryable, and actual client/semaphore disposal remains. Existing two component and three native ordinary cases pass. |
| 1061 | Remove the prepared repeated-source-disposal fallback and duplicate registry. Ledger reservations and tracked settlement retain every slot/source through deletion and recovery. Preserve all four lifecycle lock scopes on a dedicated object. Npgsql's pinned disposal latch makes the old repeated fallback unable to add or repair another source operation. |

Five bounded decisions bring the investigation map to 38 decisions and 10
unresolved questions. This does not claim 38 production bugs or complete any
additional issue checkbox. Historical mutation statuses remain unchanged.

## Preparation and evidence correction

Each criterion reaches owning GREEN, coverage inspection, production/test
refactor review and a canonical fast checkpoint before the next criterion.
These tests characterize existing behavior; the source changes are intended
to preserve behavior. No new product-repair RED is claimed.

The first cancellation hypothesis expected no model construction after
`OnConfiguring` canceled acquisition. Actual EF options initialization already
builds the model before the targeted guard. That initial test, source binding,
failure and coverage are retained as an oracle-placement correction, not a
production defect. The original criterion-2 plan is historical and superseded
by `context-oracle-correction.json` and `criterion-2.json`.

The corrected test observes the public virtual `DatabaseFacade.EnsureCreatedAsync`
operation and delegates to the real provider. It preserves a valid model and
actual bound Npgsql source. Its assertion concerns later schema initialization;
it makes no claim that this guard can suppress model work already inside EF.

Both exact guard faults were repeated on the final source. They reject the
intended assertion with 25 and three controls passing, respectively. Explicit
Compile inventories select each fault and exclude the original target source.
Primary source, binaries and dependency identities remain unchanged. The
isolated reusable build is restored to ordinary final inputs, excludes private
probes, and passes four consumer/closure checks afterward.

## Ownership proof and simplification

The ordinary flag was written only after slot cleanup and both dependency
disposals succeeded. It never serialized overlapping first calls. Removing it
adds no concurrency-safety guarantee; actual factory and cleanup ownership
continue to cache or serialize drain. The default Docker configuration owns
anonymous credentials, and exact-version HTTP/semaphore disposal semantics
support repeated completion. No artificial throw-on-second-disposal fake is
used to manufacture a distinction.

Prepared `ReserveSlot` returns its source-bearing slot synchronously into the
ledger under its lock. Ready, leased, parked and pending lifecycle states retain
the reservation. Failed deletion retains it; successful deletion disposes the
source before ownership is released. Rewarm replaces the source on the same
rooted slot and publishes settlement before launch. Drain stops admission,
joins work, resnapshots and attempts each remaining resource. Deletion invokes
source disposal before administrative SQL or DROP. Npgsql 10.0.3 latches its
sealed disposal entry before cleanup, including a failing cleanup; subsequent
calls return immediately.

The removed registry had no independent diagnostic, resource-ID or lifetime
consumer after its fallback was removed. Its locking role remains explicit in
`_lifecycleGate`; the ledger, host and drain semaphore keep their separate
ownership roles. This differs from the factory's retained unpublished-context
root established in earlier evidence. Similar collection shapes alone do not
establish equivalent lifetimes.

Exact dependency sources, source hashes, package commits, output identities and
licenses are retained. The proof covers actual public producers and normal
clock operation. It excludes injected clocks throwing before deletion,
reflected corruption, fatal allocation failures and arbitrary substitute source
types. It does not claim an initial data-source disposal cannot fail.

## Verification and limits

Final owning coverage passes 26 rows: seven fast and 19 actual native cases.
These protect diagnostic/cancellation boundaries, repeated ordinary closure,
late material failures, held rewarm, verified replacement, failed DROP, host
retention, startup failure and successful retry. The native inventory matches
before and after. Relevant retained validation, cancellation, lifecycle locks
and cleanup sequence points execute. Coverage proves execution; assertions and
separate producer/dependency proofs justify the claimed outcomes and removals.

Whole-session mutation remains pending after the final coding-session snapshot.
Bounded fault calibrations do not establish a fresh mutation score. Source and
evidence remain local with no code remote or hosted CI claim. Caches, evidence
and unknown baseline resources are preserved. Final independent verdict belongs
in `completion.json` after review of the frozen complete candidate and envelope.

## Final ordinary gate

**310 fast + 170 PostgreSQL = 480 passed**, no failures/skips. Total 610.041s. Solution and separate example builds pass. Exact discovered/executed IDs, source/binary/dependency identity, stopped owned process groups and matching before/after labelled Docker inventories are retained. The final 26-row owning coverage binds identical inputs. Independent final verdict is recorded separately in `completion.json`.

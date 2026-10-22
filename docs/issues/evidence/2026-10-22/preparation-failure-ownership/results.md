# Preparation failure ownership through cancellation and shutdown

## Accepted scope

This bounded TT023/TT024 slice retains meaningful unseen preparation and
compensation errors until verified publication or actual delivery. It covers
initial and replacement preparation in fixed owners, actual ordinary adapter
cancellation, retry cadence and stopped recovery. Both issues remain open for
their broader unchecked criteria. GitHub owns live workflow and acceptance.

Production extends local base `5f2247f`. The existing reservation marker, gate,
verified publication clearing, delivery identity and drain consumption remain
the ownership mechanism. No resource state or retry budget was added.

| Criterion | Implementation and evidence |
| --- | --- |
| Fixed replacement error survives interrupted retry | Two controlled-clock rows reproduce missing drain failures with and without an observing caller. Material preparation errors now acquire ownership without an adaptive-policy restriction. |
| Later completion settles prior errors correctly | Verified successful publication clears the episode; later owned cancellation preserves its prior material error; failed compensation retains both prior error and new cleanup error through the same exception identity. |
| Abandoned initial preparation | Four gated rows distinguish material error, failed compensation, both, and pure owned cancellation. Three genuine missing-exception REDs establish the loss; cancellation with confirmed deletion remains a clean control. |
| Successive material errors | A later distinct error no longer overwrites the earlier unseen error. A genuine missing-cause RED establishes the distinction; safe aggregation retains both and terminal delivery clears the same aggregate identity. |
| Independent failures and delivered-terminal deduplication | Two waiting acquisitions receive the same first pool failure; a distinct second preparation failure remains for drain. Existing finite three-candidate recovery-budget evidence protects composed-error delivery and successful later retries. |
| Actual ordinary cancellation classification | Two real Docker/PostgreSQL cases induce a reset rejection with SQLSTATE P0001, hold the new created container before start, then close the owner. Token-associated cancellation drains cleanly; a late IOException retains ContainerPrepare identity. Both actual containers are deleted once. |
| Retry cadence and stopped admission | Positive timer gates bound retries; StopAcceptingWork suppresses automatic cleanup scheduling while another reservation remains leased. Exact original 1485 delay-removal and 1609 OR-to-AND faults fail named assertions promptly, with controls passing. Both original predicates remain. |

## TDD and evidence corrections

Six criterion records retain their commands, source/binary/dependency cuts,
TRX and coverage. A completed criterion's canonical fast checkpoint precedes
the next criterion. Actual product REDs include missing exceptions, missing
earlier causes and falsely reported owned native cancellation. Copied-source
guard faults are bounded assertion calibrations, not a Stryker replay.

The initial broader retention change exposed a delivered-terminal duplicate:
preparation cleanup created a new exception while the retained marker still
pointed at its old cause. The existing recovery-budget test caught it. Error
composition and retention now pass the same safe instance onward. A later
test proves that composing a canceled candidate's cleanup from the retained
material cause preserves the earlier meaningful failure.

One fixture initially reused Reservation.Id as a physical resource identity.
Actual producers create distinct candidate resources within a stable reservation.
The corrected fixture allocates unique resource identities. Its earlier duplicate
deletion failures are retained as setup failures, not product REDs.

The existing concurrent-preparation test expected drain success after two type-only
acquisition failures. Source and a stronger oracle show that both acquisitions
receive the first failure; the second remains undelivered. Distinct safe stages
and types now prove that drain reports the second once while preserving the
original cleanup concurrency, deletion and reservation assertions. The old
canonical failure remains recorded; the test was not weakened to suppress it.

## Coverage, diagnostics and resource boundaries

Final owning coverage selects all new component cases, the independent-candidate
and terminal-budget controls, and all OrdinaryRecoveryTests. It executes 16 rows
against the final source and runtime inputs. Safe stages/types and SQLSTATE are
asserted without arbitrary prose/order, raw inner causes or private payloads.

Fast tests use actual slot-ledger logic and controlled collaborators. The native
cases separately exercise actual reset failure, Docker ownership of a created
unstarted container, cancellation/material classification, deletion, once-only
error delivery and subsequent clean disposal. Neither lane substitutes for the
other. Every native run retains matching before/after labelled Docker inventory.

## Original question map and limitations

Two additional bounded decisions for original questions 1485/1609 bring the
historical map to 30 decisions and 18 unresolved questions. This is investigation
evidence, not a second workflow tracker. The preparation defects were reproduced;
the original 48 reported entries are not retroactively declared production bugs.
Original mutation statuses remain unchanged. Whole-session mutation is pending
after the final coding-session snapshot, separately from ordinary publication.

The slice does not claim all initializer timeout domains, every adapter failure,
the remaining optional callback/ledger questions or every meaningful lifecycle
failure path are complete. No test timeout, retry count, lane, concurrency or
PostgreSQL setting was tuned. Local source commits only; no code remote or hosted
CI result is claimed. Reusable caches and the unknown pre-existing stopped
container remain. Test-created resources were removed by their owners; retained caches and unknown resources were preserved.

Independent preliminary review has no actionable source/test finding. Final
completion requires review of the frozen complete candidate and exact envelope;
its verdict is recorded separately in `completion.json` after PASS.

## Final ordinary gate

**306 fast + 170 PostgreSQL = 476 passed**, no failures/skips. Total 615.363s. Solution and separate example builds pass. Exact discovered/executed IDs, source/binary/dependency identity, stopped owned process groups and matching before/after labelled Docker inventories are retained. The final 16-row owning coverage binds identical inputs. Independent final verdict is recorded separately in `completion.json`.

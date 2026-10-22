# Factory observer ordering and completed-node lifetime

## Accepted scope and claims

This bounded slice advances TT-024's original retained-state questions and
TT-023's already accepted maintenance-failure ordering. Both issues stay open.
The original 48-question map now has 20 previously accepted decisions, eight
additional bounded decisions here, and 20 unresolved questions. GitHub remains
the only live workflow authority; this count describes the historical inquiry.

Production code is unchanged from `8f572b8`. The existing success-or-delivered
predicate and observer, worker, acquisition and drain removals are useful.

| Question / criterion | Evidence and decision |
| --- | --- |
| 1906: canceled request, actual failure recorded, then observer finally, then drain | New maintained `A_failure_recorded_before_canceled_observer_finally_remains_available_to_drain` holds actual deletion, captures the causally first cancellation post, records actual failure through a test-owned FIFO fence, then releases the observer. Public cancellation and safe SlotIdleRetire failure, cleanup capability, exact reservation/deletion settlement and cached once-only drain failure are asserted. Keep the existing predicate. |
| 1902 | One-off omission retains bare maintenance settlement TCS before and after drain; stale admission membership also prevents late successful worker removal. Keep observer ledger removal. |
| 1905 / 1907 | Changing success-or-delivered to AND or suppressing observer removal retains observed success/delivered failure work while factory remains open. The two faults suppress the same useful removal for these actual producers; neither is equivalent to baseline. |
| 1925 / 1927 | Changing worker predicate to AND or suppressing its removal retains successful work after fully observed cancellation until drain. Completion signalling and the predicate share the gate, so the observer cannot mark Delivered before this worker check. Keep late-worker removal. |
| 2002 | Omitting lease acquisition settlement removal retains its bare TCS even though public context/lease/task collection can pass. Keep the removal. |
| 2055 | Omitting drain work removal retains previously undelivered failed work after its safe error is reported and resource disposal succeeds. Baseline releases it; cached disposal does not replace collection cleanup. Keep the removal. |

## TDD, source binding and calibration

The new test characterizes supported production behavior and passed on
introduction. No production repair RED is claimed. The former private
CleanupPump was extracted into shared test-only QueuedSynchronizationContext;
the previous root-lifetime contract remains independently fault-sensitive.

Final source-bound owning coverage executes all 16 factory maintenance and
backpressure rows. The exact 1906 predicate fault fails only the new race with
the intended missing-failure assertion, while three ordering/root controls pass.
The 1941 root-removal control fails only the abandoned-acquisition lifetime test
with premature finalization; three controls pass. Every owned process settles
without a deadline overrun. The compound guard executes even though its removal
statement is intentionally not taken for undelivered failure.

One-off `PrivateNodeProbe` is compiled only into an isolated diagnostic test
assembly. It captures existing nodes weakly under their real gate, exits all
strong reflection/operation/drain frames before bounded collection, and keeps
the factory alive. Nine compiled variants (baseline, seven faults, unchanged
control) exercise five scenarios each. All 45 rows match independent expected
node-lifetime patterns, safe public drain stages, one resource creation and
confirmed disposal, zero remaining reservations, and finite deletion counts.
These structural probes are not maintained private-field regression tests or
measured long-running memory-growth bounds. Npgsql sources perform no SQL I/O
in these probes; the ordinary native gate separately retains the real boundary.

## Corrections retained

- The initial cancellation ran within the captured context and executed inline;
  that setup timed out waiting for a post. Cancellation now runs on a separate
  joined task; observation bounds are unchanged. This was not a product RED.
- Independent review found the old unconditional next-callback assumption could
  consume a 100 ms recovery timer. A test-owned FIFO fence now completes already
  posted work without claiming opaque callback identity. The final new race is
  about 5.5 ms, and both source-fault controls were rerun after this repair.
- Raw statement-removal probe substitutions raised CS0642 under warnings-as-errors
  for empty if/lock bodies. Equivalent empty blocks normalize those diagnostic
  faults. Failed builds and prior source cuts remain in the archive; only the
  complete final nine-variant run supports the accepted structural decisions.

## Limits and remaining work

Original Stryker statuses are unchanged. Copied-source fault calibrations and
structural probes are not a fresh whole-session mutation run. That run remains
pending until the final coding-session snapshot. Both TT-024 and TT-023 remain
open for unchecked criteria, including remaining ledger, optional callback,
late cancellation and independent-failure decisions. No private-only probe is
presented as a complete public lifetime contract.

No verification deadline, lane, concurrency or PostgreSQL setting was changed.
No code remote exists; publication is a local source commit. The pre-existing
unknown stopped container and reusable caches remain. Exactly 172 disposable
historical raw coverage files (439,719,464 bytes) were removed only after each
original and its committed archive member were verified against the manifest.
Their archives, TRX, source bindings and rebuildable caches remain available.
The cleanup receipt names every removed path and recovery archive member.

Independent preliminary review accepted the FIFO repair and reconstructed all
45 structural rows. Its final verdict must bind the frozen complete candidate,
full ordinary gate and exact post-review envelope; that verdict is recorded
separately in `completion.json` only after PASS.

## Final ordinary gate

**295 fast + 168 PostgreSQL = 463 passed**, no failures/skips. Total 675.390s. Solution and separate example builds pass. Exact discovered/executed IDs, source/binary/dependency identity, stopped owned process groups and matching before/after labelled Docker inventories are retained. The final 16-row owning coverage binds identical inputs. Independent final verdict is recorded separately in `completion.json`.

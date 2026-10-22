# Interrupted startup retains actual initializer failure

Owning issue: [TT-023](https://github.com/DanMarshall909/TurboTest/issues/13). This is a bounded repair; the issue remains open.

## Behavior and evidence

Startup previously returned `StartupCanceled` or `StartupTimeout`, then unconditionally swallowed a later initializer failure under the assumption it had already been reported. Cleanup could succeed physically without exposing that material error.

The repair captures a distinct late material initializer error, preserves its existing safe stage/SQLSTATE, waits for actual initializer/cancellation/disposal completion, and delivers it once through retained cleanup. It neither replays the already-observed initializer error nor converts successful/owned-cancellation completion into a new material error.

The ten-row fast theory reuses the actual generic slot ledger. Six material rows genuinely fail before the product change; four controls pass. The assertions independently specify raw type/safe stage/SQLSTATE, cleanup identity, actual deletion, empty reservations and repeat recovery without duplicate errors/deletions.

The native seed case issues a real PostgreSQL query only after startup cancellation and while cancellation callback completion is independently held. It receives `BaselineInitialize`/`42P01`, exposes no query text, retains the original container until work and callback completion, deletes it once, and retries cleanly. Existing native success and callback-failure rows remain distinct controls.

The final isolated compile fault consumes the actual initializer error. Seven intended assertions fail, including the native seed case;13 controls pass. The primary compiler/runtime/dependency inputs remain unchanged; the warm isolated build is restored to original sources and passes. The initial unused-variable calibration compile failure is explicitly retained as setup failure.

## Coverage and refactoring

Final owning coverage contains339 rows (327fast,12native) with exact IDs matching final discovery. New capture/report lines execute; diagnostic and physical-resource assertions, rather than coverage alone, establish behavior. Production retains one owner/delegate and neighboring once-delivery flags; no extraction earns its cost. Tests reuse one bounded actual slot fixture and the existing native held-work/callback helper, without another fake or private seam.

Final ordinary verification: **327fast+171PostgreSQL=498passed**, no failures/skips. Solution and separate DynamicScalingExample builds pass. Source, runtime and dependency identities match owning coverage and final calibration. Native resource inventories preserve the unrelated stopped baseline; owned command groups stop. `completion.json` retains the independent bounded-slice verdict.

## Limits and remaining work

This does not complete TT023 or reclassify any of its54original generated faults. Immediate initializer-owned timeout classification, caller/simultaneous shutdown precedence, integrated secondary safe failures/capabilities and remaining original question decisions retain their scope. Raw OperationCanceledException handling is unchanged; foreign-token and all AggregateException-inner taxonomies are not claimed.

The new fast deadline rows add roughly250ms of intentional waits; the native seed case uniquely calibrates real user seed, provider errors and Docker deletion. No lane changes are inferred from a single timing variation. Whole-session mutation remains pending after the final coding snapshot. Native Project fields are unverified/unmodified. Source and evidence remain local under the declared repository policy; no source push or resource/cache deletion.

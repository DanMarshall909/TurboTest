# TT024 queued retry and optional callback contracts

## Scope and decisions

This slice advances TT024's retained ownership, late cancellation and optional internal-domain investigation. It addresses original questions 1448, 1372, 1373, 1374, 263 and 1265. Thirty-eight prior bounded decisions are already integrated; these six candidate decisions leave four questions: 466, 1202, 1208 and 1211. The GitHub issue and its unchecked acceptance criteria remain open. Historical mutation statuses are unchanged.

| Question | Observable contract or reconstructed invariant | Evidence |
| --- | --- | --- |
| 1448 | A completed retry timer whose continuation is held until actual ledger closure cannot invoke another resource factory. The two actual created resources are deleted once; optional backend preparation does not run again. | New `A_retry_queued_before_shutdown_cannot_create_another_resource_after_closure`; exact early cancellation omission fails factory count, expected 2/actual 3, with 11 controls passing. |
| 1372/1373 | `ClaimParked` callers hold the same lock. Acquire rejects terminal state before enqueue; Tick rejects closure/stopping and failure cannot enter growth; Park completion is nonreplenishing after every terminal transition clears waiters. | Independent source trace plus actual acquire/growth/park/recovery/drain coverage. Remove only repeated terminal checks, keeping capability/demand/admission checks. |
| 1374 | Without rewarm capability, a parked resource stays untouched while acquisition prepares spare physical capacity. Both actual identities are deleted at final closure. | New `Parking_without_a_rewarm_capability_retains_the_parked_resource_and_prepares_spare_capacity`; corresponding null-capability omission fails parked-resource deletion assertion, with 8 controls passing. |
| 263 | Evaluate observes its supplied snapshot and starts pressure timing even without a separate transition notification. Ledger event notifications continue to capture interruptions between evaluations. | New `Evaluation_starts_the_pressure_window_from_its_current_snapshot`; exact observation omission changes structured GrowthStabilizing to NoDemand, with 19 controls passing. Existing interrupted-between-ticks test protects the other responsibility. |
| 1265 | A nullable-park policy tick can defer physical shrink. Later explicit maintenance subtracts leased/non-Ready warm commitments from the target: target 2 with leased 1 preserves Ready 1 and parks one. | New `Deferred_shrink_maintenance_counts_leased_commitments_toward_the_remaining_warm_target`; exact addition fault fails parked count, with 8 controls passing. |

## Domain and ownership

The actual internal ledger and policy APIs have optional callback combinations that are useful for direct component consumers and tests. Public ordinary producers expose no parking; public prepared producers supply park/rewarm callbacks. The optional cases above are not assertions of public producer reachability. Retaining a coherent existing internal capability is preferable to narrowing an API solely to erase a diagnostic distinction.

The terminal-check removal covers current callers/transitions and supported default or nonreentrant clocks. It does not cover arbitrary internal clocks that reenter owner shutdown during policy evaluation. All terminal setters (`FailAfterConfirmedDeletion`, `Quarantine`, `DisposeAsync`, `StopAcceptingWork`) clear waiters under the same ledger lock; no later acquisition can enqueue. The retained nonreplenishing empty-waiter check therefore rejects parking-completion claims after any terminal transition.

The early retry cancellation check is independently useful at the ledger boundary. The public factory also stops admission, which independently prevents reservation. Later canceled preparation-permit checks prevent backend work even if resource creation were incorrectly attempted. No post-close Docker CREATE claim is made.

## TDD, corrections and refactoring

All four new tests characterize existing behavior. No new product-defect RED is claimed. Each criterion completed owning GREEN/coverage, rejected one bounded compiled fault where applicable, considered production/test simplification and passed the canonical fast driver before the next criterion. Three useful checks and the warm-floor arithmetic remain; only source-proven repeated terminal checks are removed.

The initial queued-retry setup failed to pump the replacement cleanup-permit continuation before observing the timer. Its failure cleanup then awaited a pending observer before closing the owner. The 60-second driver deadline stopped owned workers; that attempt is preserved as a setup/liveness correction. The final test pumps to the actual timer request and closes the owner before observing waiters in failure cleanup. The unavailable XPlat collector was replaced with the installed Code Coverage collector. Neither correction is product RED or fault calibration.

The first capability-calibration reader expected 10 rows from a copied count; exact raw discovery/execution contained 9. The same run was inspected and counted without restarting; only its new test fails and eight controls pass. Final-source calibration uses the corrected count.

Tests reuse the existing real ledger/policy, manual clock, held retry clock and FIFO synchronization context. Resource callbacks carry actual identities and finite invocation/deletion oracles. No private collection reflection, production seam, package, lane, timeout, concurrency or resource-budget change is introduced. Existing teardown and whole-process deadlines remain part of ordinary verification.

## Verification limits

Final-source calibrations are isolated compiled-source substitutions with explicit compile inventories, runtime hashes and unchanged primary source/binary/dependency checks. Original 1448, 263 and 1265 operations are reproduced exactly on current source; 1374 is the corresponding lost-null-capability fault after repeated terminal checks were simplified. This is bounded fault calibration, not a Stryker replay, fresh historical status or whole-session adequacy score.

The reusable isolated calibration build is restored to original final sources and all four new facts pass before ordinary reuse. Owning coverage and the complete ordinary gate bind identical source, runtime and resolved-dependency inputs. Native inventories must match the retained unknown stopped baseline. Whole-session mutation remains pending on the final coding-session snapshot. Source commits remain local under the repository policy.

The initial test correction is a local scheduling/cleanup lesson already covered by finite-oracle and failed-cleanup guidance; no standing rule or memory change is proposed.

## Final ordinary gate

**314 fast + 170 PostgreSQL = 484 passed**, no failures/skips. Total 584.744s. Solution and separate example builds pass without warnings/errors. Exact discovery/execution IDs, source/runtime/dependency inputs, stopped owned command groups and matching labelled Docker inventories are retained. Final owning coverage has 65 passing rows (53 fast, 12 native) on identical inputs. Independent verdict is recorded separately in `completion.json`.

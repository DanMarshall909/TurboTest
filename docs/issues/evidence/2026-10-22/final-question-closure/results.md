# TT024 final bounded investigation and closure

## Scope and final decisions

This concludes the 48-question investigation owned by [TT024](https://github.com/DanMarshall909/TurboTest/issues/14). The exact identity/status map preserves every original reported mutant status. Forty-four bounded decisions were integrated through `5f827c3`; four final decisions complete the map. A question or generated fault is not automatically a production defect. Final whole-ticket review and the approved archival/tracker envelope remain separate from this proposed record.

| Question | Contract and implementation decision | Final-source executable evidence |
| --- | --- | --- |
| 466 | Retain `SingleOrDefault`: a valid alternate provider must reach safe ContextConfiguration ownership rejection, with context/source discarded once. | New real-provider positive save/query followed by factory rejection; exact `Single` fault fails only owning Stage assertion; three Npgsql binding controls pass. |
| 1202 | Retain strict target increase: Held decisions start no physical work. Target bounds admitted commitments; new demand can fill an intentionally discarded deficit. | New actual ledger creation/deletion identities; Held leaves zero reserved, subsequent demand leases distinct replacement. Exact `>=` fault creates early and fails owning assertion; ten final adaptive controls pass. |
| 1208 | Retain actual-decrease AND park-capability gate. Held capacity monitoring does not perform idle retirement. | New held-at-minimum expiry scenario; exact OR fault deletes parked identity early and fails owning assertion; six positive controls pass. |
| 1211 | Retain strict target decrease: explicit maintenance or actual shrink applies expiry, with reservation retained until deletion confirmation. | Same scenario then holds actual explicit deletion, observes reservation retained and confirmed release. Exact `<=` fault fails premature-deletion assertion; six controls pass. |

The policy cadence was selected explicitly before the tests, from the existing distinction between admitted target changes and physical maintenance. TT008 and `IdleDatabaseLifetime` already define age as eligibility for idle maintenance; TT009 adds stabilized shrink through that lifecycle. Neither previously promised every-tick expiry or automatic refill after no-waiter discard. The README and API documentation now state the selected behavior and tell callers to schedule `MaintainIdleAsync` when they need cleanup at an unchanged target. No functional production code changes in this slice.

The OR fault also violates the retained internal nullable-park contract: it can claim work that cannot execute and leave an orphaned settlement. That additional source-level distinction is preserved explicitly. The bounded runtime calibration uses the new nonnull-park expiry scenario and positive controls, so its named assertion and cleanup terminate promptly. No normal-suite or mutation exclusion is added; no runtime proof of the orphan schedule is claimed.

## Genuine alternate provider

The test-only [EF InMemory provider](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/) is used as a genuine rejected provider, not a PostgreSQL/relational test double. Its [10.0.12 package](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.InMemory/10.0.12) matches the repository's EF version. A standalone context saves and queries a real row before the same context type ignores supplied owned options and selects its own valid provider. This prevents missing-provider/invalid-model setup failures from impersonating the intended guard. The pinned package/nuspec/license/runtime binding is retained; production dependencies remain unchanged.

## Acceptance audit

| Issue clause | Accepted bounded evidence |
| --- | --- |
| CTS, registration and completed-work lifetime contracts | Maintained initializer/monitor gates, registration and cleanup-payload collection, repeated public context/lease/task release and pending context rooting; distinct one-off weak-node/SDK/provider diagnostics. |
| Dominated fallback and optional invariants | Actual producer/dependency proofs justify removing dominated fallback/registry/completed state and repeated caller checks. Meaningful snapshot/capability/warm-floor/cancellation checks and strict capacity gates remain. |
| Cleanup-capability precedence | Maintained identity, union/composition, overlapping/shared retry and real multi-owner recovery contracts. |
| Gated reentry and late-cancellation ordering | Finite internal reentry/observer laws, actual initializer/monitor gates, FIFO canceled-observer/actual-failure ordering and queued retry cancellation, all with retained external deadlines. |
| Every linked question resolved | Exact 48-identity map: 44 prior accepted plus four final bounded decisions, no omitted/added identity or rewritten historical status. |
| Prioritize ownership decisions | Recorded source sequence established capability ownership/reentry before later completed-node/optional work. TT022/023/027 retain their own independent scope. |
| Repeated successful operations release references | Maintained 16-cycle public lifetime/registration laws plus distinct 45-row one-off private-node diagnostics. Public payload collection is not substituted for internal work-node release evidence. |

`closure-audit.json` names every clause, its evidence and its limits. All eight prior completion records report independent PASS. An independent source audit and root revalidation both matched every prior archive size/digest and all 2,992 members. Those historical runs retain their original source cuts; current integrated verification is recorded separately.

## Limits preserved at closure

- The lifetime claims are named bounded contracts, not a universal leak/memory-growth guarantee. Provider question 2218 proves handle disposal during successful callback/actual drain, not every cancellation-failure exit. TT023 remains open for its separate scope.
- Internal owner/deletion-adapter reentry (1691) and legitimate observer completion (2096) do not establish a deterministic native scheduling theorem. Structural reflection is confined to one-off diagnostic evidence; maintained tests assert behavior, identities and actual resource recovery.
- Producer simplifications cover actual callers and default/nonreentrant clocks, not arbitrary internal reentry, reflected corruption or fatal allocation. Useful context dictionary ownership and actual disposal/retry paths remain.
- The new tests characterize existing behavior after explicit contract decisions. No new product-defect RED, historical Stryker replay, updated historical mutation status or fresh adequacy score is claimed. Whole-session mutation remains pending after the final coding snapshot.
- Source/evidence remain in this local checkout; no configured code remote or push is involved. Native Project APIs and browser access are unavailable, so the envelope updates only the issue checklist/body/state. Project fields remain unverified/unmodified.

## TDD and refactoring

Each final criterion completed owning GREEN/coverage, named compiled-fault rejection, production/test refactor consideration and the canonical fast driver before the next criterion. Production retains useful guards and clarifies their contract; no duplicate policy or new production seam is needed. Tests reuse actual ledger/policy/source fixtures, finite creation/deletion callbacks, monotonic manual time and a held deletion. The alternate provider is a test-only dependency with an independent positive oracle. No existing deadline, lane, concurrency, budget or PostgreSQL setting is weakened.

Final source calibrations bind explicit compiler inventories/runtime inputs and leave primary source/binary/dependency identities unchanged. The reusable isolated build is restored to original final sources, with the three new facts passing before reuse. Owning/full-run source, runtime and resolved dependency inputs must match exactly. Native inventories preserve the unknown stopped baseline. The final independent verdict and exact archive/commit/tracker envelope are recorded separately.

## Final integrated ordinary gate

**317 fast + 170 PostgreSQL = 487 passed**, no failures/skips. Total 705.422s. Solution and separate example builds pass without warnings/errors. Final owning coverage passes **338 rows (317 fast,21native)** on identical source/runtime/dependency inputs. Exact discovery/execution IDs and resource inventories match. The independent whole-ticket verdict is in `completion.json`.

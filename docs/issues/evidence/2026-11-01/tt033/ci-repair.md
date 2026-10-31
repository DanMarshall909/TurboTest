# Hosted CI failure and bounded fixture repair

TT-033's first hosted [CI run](https://github.com/DanMarshall909/TurboTest/actions/runs/41252036297)
for candidate `44bb7ff3a4022b6fd02b790de1056e92a081a082` failed: 364 of 365 fast
cases passed. The slow lane did not execute. The failing unchanged case was
`FactoryBackpressureTests.A_blocking_cancellation_callback_cannot_hide_the_drain_deadline_or_its_later_failure(lateFailure: False)`.
It timed out at line 575 waiting two seconds for the injected callback to enter;
its factory drain assertions had not yet run. The original local all-lanes
verification had passed all 541 cases. Neither fact establishes a hosted pass.

The original artifact and failed-job log are preserved in the supplement. The
ZIP's SHA-256 matches GitHub's artifact metadata:
`a5fb0bc84e53917aca7a67e0f6cd3ab25f2e2f50db29eed0ebe4348c57ad9436`.
The failed attempt was not rerun to obtain a green status.

## Diagnosis and repair

The private `BoundedPool` test fixture registered its injected cancellation
callback before registering acquisition cancellation. Cancellation could complete
the acquisition first. Its asynchronous continuation could then leave
`GetDatabaseAsync` and dispose the injected registration before dispatch reached
that callback. This makes the test's callback-entry precondition unreliable.

An isolated diagnostic uses the actual `TtSlotPool` and an immediate
`SynchronizationContext.Post` to force that continuation ordering. With the old
registration order, cancellation completes and the injected callback count is
zero. With acquisition registered first, cancellation completes and the callback
count is one. This reproduces the mechanism; it does not reconstruct the hosted
runner's exact scheduling. The first diagnostic build failed because sandboxed
NuGet vulnerability-data access was unavailable; its log is retained separately
and is not counted as behavioral RED evidence.

The repair moves acquisition creation before the injected callback registration,
then publishes `Waiting` after both are registered. Existing test cases,
assertions, two-second observation limits and the 100 ms drain deadline are
unchanged. No production, experiment, workflow, dependency or lane code changes.
The two existing theory cases still check deadline reporting, retained ownership,
late-error delivery, retry cleanup, closed admission and callback cardinality.
This repair also preserves the neighboring throwing and reentrant callback cases.
No new test mirrors fixture implementation; the one-off diagnostic supplies the
adversarial ordering evidence. No production refactor is needed, and the fixture
refactor is limited to establishing its intended precondition.

## Validation and provenance

The original `validation.json` and `validation-results.tar.gz` describe the
pre-repair checkpoint and are intentionally unchanged. The supplement records
verification for the repaired maintained test source and preserves the hosted
failure, diagnostic source/output and final complete gate. The frozen experiment
source at `60a35485283e7cb7703de01eb443ef2c6ab1a2cb`, all ten measurements and their
analysis remain unchanged. The benchmark conclusion remains negative.

The repaired source passed all **541 maintained tests (365 fast / 176 slow)**,
with zero failures/skips and unchanged resource inventory. Complete driver wall
time was 771.409 s; lane wall times were
4.519 s fast and 752.727 s slow.
Final details and every supplement-member hash are recorded in
[ci-repair-validation.json](ci-repair-validation.json). Exact-head
hosted CI and renewed independent GPT-6 Astra review are recorded on PR #28;
this document does not substitute a local result for either publication gate.
There is no changed production area and no new coverage or mutation score is
claimed for this fixture-only repair.

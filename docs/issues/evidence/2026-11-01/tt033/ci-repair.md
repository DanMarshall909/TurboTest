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

## Second hosted attempt: startup callback lifetime

The new [hosted run](https://github.com/DanMarshall909/TurboTest/actions/runs/41253502713)
for first-repair candidate `04ec654ae9b1f79922b954fbbd234284d70fc101` also failed:
364 fast cases passed and one different, unchanged case failed. Both repaired
blocking-callback cases passed. The failing case was
`StartupLateDrainTests.Startup_recovery_reports_cancellation_drain_and_retry_failures_together_before_successful_recovery`:
its assertion at line 427 did not find `StartupCancellation`. The slow lane did
not execute. Its original ZIP matches GitHub's SHA-256
`60fd81c274cf5436c9aac80cd64e77f8556c7af592c2e170e0c658c5fa29b9c4`.
This failed run also remains intact; it was not rerun for a green result.

Startup deliberately reports interruption before its asynchronous cancellation
callbacks have necessarily run. This test released its initializer immediately
after interruption, allowing the initializer's `using` registration to be
disposed before the injected throwing callback executed. The test could thus
remove its own failure precondition.

A second isolated diagnostic uses actual `Tt.InitializeOwnedPoolAsync` and
`TtSlotPool`, with a gated cancellation dispatcher. Releasing initialization
before dispatch invokes zero injected callbacks and reports no callback failure;
waiting for callback entry invokes one and preserves `StartupCancellation`.
Both schedules reclaim all owned slots. This establishes the mechanism under a
forced schedule without claiming the hosted runner used exactly that ordering.

The second repair adds a callback-entry signal and waits for it before releasing
initialization. Existing assertions and timeouts remain; the new precondition
uses the existing two-second observation limit. No production, experiment,
workflow, dependencies, native tests or lane membership changed.

Current source passed the complete **365-case fast lane** locally, with zero
failures or skips. The earlier 541-case local result belongs to the first repair
and is not represented as a full run of this second repair. The final candidate
must pass the complete hosted fast and slow lanes before merge, in addition to
renewed independent review. [The second supplement](startup-repair-validation.json)
retains that distinction, the failed hosted artifact and logs, diagnostic
source/output, fixture patch, source bindings and local fast verification.
The original benchmark protocol, measurements, analysis and stop decision remain
unchanged. No new performance variant was attempted.

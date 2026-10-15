# Post-session mutation review — 2026-10-15

The two complete eligible mutation runs have been reviewed against source commit
`29e974adc59e36ef8622f5f2c176ddb563f8c6d4`. Every one of the **946 requested
entries** has an exact, disjoint classification in [classifications.json](classifications.json):
403 Survived, 167 NoCoverage, 111 Timeout and 265 CompileError. The review opens
18 focused quality/investigation issues; it makes no production changes and
claims neither 403 production bugs nor complete mutation adequacy.

The original ten implementation tickets remain resolved. The later documentation
commit `e3e5958` changes no compiler/test inputs. All 140 frozen input hashes and
all 34 EFCore plus nine benchmark report source files match the current source.

## Report and selection integrity

| Assembly | Killed | Survived | NoCoverage | Timeout | CompileError | Ignored | Execution |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| EFCore | 659 | 306 | 107 | 108 | 219 | 873 | Exit 0, 2,016.51 s |
| Benchmarks | 264 | 97 | 60 | 3 | 46 | 426 | Exit 0, 186.15 s |

Both reports retain all 267 unique test IDs from the ordinary isolated fast TRX,
with exact ID-set equality. Their named catalogs contain 145 method names with
theory-row multiplicity; stripping arguments and comparing multiplicities also
matches the ordinary fast membership. No slow method appears. Source-resolvable
linked IDs number 224 in EFCore and 43 in benchmarks; both have zero unlisted
link IDs. Catalog presence alone is not participation, and early killing does
not imply all catalog tests ran against every mutant.

The admitted scope uses Stryker.NET 5.0.0, per-test coverage, fast tests only and
228 calibrated conservative UTF-16 exclusions. Neither-covered source remains
visible. Full original-node containment means a whole-block mutation can cross
slow-covered statements and uncovered braces and therefore remain eligible.
Such NoCoverage entries do not demonstrate a missing ordinary PostgreSQL test.
Covered conditional lines likewise do not prove their throw/return body ran.
No source exclusions, thresholds or test selection have been weakened.

## Survivors and missing coverage

| Classification | Survived | NoCoverage |
| --- | ---: | ---: |
| Distinguishable selected-lane assertion gaps | 217 | 37 |
| Unexecuted owning-contract coverage gaps | 2 | 58 |
| Ordinary assertion gap at a slow boundary | 0 | 1 |
| Existing ordinary slow/boundary protection | 57 | 29 |
| Equivalent in the stated supported domain | 36 | 10 |
| Unowned wording or presentation | 31 | 16 |
| Presentation policy, without equivalence claim | 9 | 0 |
| Contract, lifetime, reachability or invariant investigation | 51 | 16 |
| **Total** | **403** | **167** |

These are counts of generated faults, not distinct defects. Every entry links to
its source span, replacement, rationale group and relevant issue. The 83 groups
combine cross-file patterns rather than reviewing only the latest changed code.
No source/runner attribution contradiction was established. In particular,
placeholder-removal mutant EFCore1431 is masked by synchronous re-addition during
sequential preparation; its question is the concurrent lock-release window.

Equivalence conclusions are bounded to the documented public-owner protocol and
pinned runtime. Examples include already validated internal arguments, safe inner
exception prose that is never exposed, positive allocation values after
`Math.Max(1, ...)`, dominated error-count branches and discarded Task payloads.
They are not permission to delete useful code or suppress entire mutation kinds.
Unproved lifetime, cleanup precedence, optional callback and randomized-order
questions are tracked explicitly rather than called equivalent.

Material patterns include accepted endpoint values, guard-masking in negative
validator tests, missing later-block scheduling cases, incomplete experiment
input manifests, lifecycle publication/late-failure interleavings, shared permit
ownership, and unexercised failure/cleanup boundaries. Existing slow tests remain
ordinary regression evidence; they will not be imported into mutation runs.

## Compiler-error investigation

All **265** entries were applied one at a time to independent syntax trees using
the pinned Roslyn 5.0.0 compiler, C# 14, the actual MSBuild source/reference graph
and conditional symbols. Both unmodified assembly baselines have zero semantic
errors. Project `NoWarn` 1701/1702 is respected when interpreting warnings.

| Standalone diagnostic result | EFCore | Benchmarks | Total |
| --- | ---: | ---: | ---: |
| Semantic compiler error | 112 | 25 | 137 |
| No errors or unsuppressed compiler warnings | 87 | 19 | 106 |
| No semantic error, warning-policy-sensitive | 20 | 2 | 22 |

The actual run log records Stryker's whole-member fallback in slot failure
retention, prepared return, monitor execution and provider creation. Its pinned
[rollback implementation](https://github.com/stryker-mutator/stryker-net/blob/6e77a3451bac4793e9c839c3ff4055c1b30ca3af/src/Stryker.Core/Stryker.Core/Compiling/CSharpRollbackProcess.cs)
confirms that unidentified mutations can cause every mutation in an enclosing
member to be rolled back. This is not proof that every rolled-back mutant is
itself uncompilable.

The 137 diagnostics include definite-assignment, incompatible operators/member
access, impossible patterns, missing returns/required members and constant
overflow. These are generated-mutant failures, not a failing production build.
The 106 clean single-fault variants are **recovery candidates**, not executed
outcomes. The 22 warning-sensitive variants require explicit reconciliation with
`TreatWarningsAsErrors=true` and Stryker's compilation/instrumentation policy.
The diagnostic harness does not run analyzers, emit/run every variant, or prove
that Stryker can recover them. [TT-011](../../../open/testing/TT-011-recover-mutation-coverage-lost-to-compiler-rollback.md)
owns that work; none is promoted to a kill.

## Timeout investigation

All **111** timeouts retain their original status and exact fault identity:
21 affect completion/publication signals; 21 corrupt permit ownership; three
corrupt retry progress; three corrupt CLI/block-index progress; three divert
provider/initialization work; three cause secondary diagnostic exceptions; and
57 alter other admission, state, cancellation, queue or shutdown transitions.

The source review explains plausible progress failures for each group. Exact
per-case runtime causality is not claimed without a trace/reproduction. One
representative is independently reproduced below; the remaining bounded
attribution and cleanup questions are explicit in
[TT-012](../../../open/testing/TT-012-bound-mutation-liveness-diagnosis-and-failed-test-cleanup.md).
A Timeout can detect changed liveness, but it is not a named assertion kill and
must not automatically be credited as evidence of adequate diagnostics.

The pinned [timeout calculator](https://github.com/stryker-mutator/stryker-net/blob/6e77a3451bac4793e9c839c3ff4055c1b30ca3af/src/Stryker.Core/Stryker.Core/Initialisation/TimeoutValueCalculator.cs)
uses `configuredRatio * (initialization + estimated execution) + additional`.
The pinned CLI [ratio default is 2](https://github.com/stryker-mutator/stryker-net/blob/6e77a3451bac4793e9c839c3ff4055c1b30ca3af/src/Stryker.Configuration/Options/Inputs/TimeoutRatioInput.cs),
and [initial testing passes that configured ratio](https://github.com/stryker-mutator/stryker-net/blob/6e77a3451bac4793e9c839c3ff4055c1b30ca3af/src/Stryker.Core/Stryker.Core/Initialisation/InitialTestProcess.cs)
to the calculator. Its constructor's optional fallback is 1.5; that is not the
CLI default. Both retained configurations omit the ratio override. The pinned
default extra budget is 10,000 ms.
The retained INFO logs do not establish every session's exact timeout allowance
or a whole-run throughput breakdown. Elapsed run time is not evidence to raise
budgets, change concurrency or narrow the required scope.

## Bounded direct checks

All checks use a fresh private Docker writable layer over the same immutable
source/tool/dependency image. Each fault's exact changed fragment and before/after
hashes are retained. No maintained production/test file was edited.

| Check | Actual evidence | Interpretation |
| --- | --- | --- |
| EFCore223, reported CompileError | `while (!token.IsCancellationRequested)` inverted; normal `dotnet build` has zero warnings/errors; named monitor test fails after 2 s with `TimeoutException` | At least one rolled-back variant is valid and already distinguished by a test. This is not a Stryker rerun/kill. |
| EFCore67, reported CompileError | Actual build fails with CS0220 after constant subtraction becomes addition | Genuine invalid generated mutation. |
| EFCore197, reported Timeout | Removing the monitor's startup signal makes the unchanged named test hang in final `JoinAsync`; external 10 s test-phase guard returns 124 after a clean build | Confirmed mutation-induced liveness failure and a failed-test cleanup deadline gap, not a setup/compiler failure. |
| Benchmark546, reported Survived | Exact rotation fault passes all 13 maintained schedule tests; a private three-block probe fails with `ScheduleShape` | Confirmed missing valid-domain oracle. |
| Unchanged control for the private probe | Same literal three-block/24-case assertion passes | The new input exposes the fault rather than an invalid expectation. |

The private schedule probe lives only in retained diagnostic artifacts; it has
not been installed as a maintained test. A future repair owns its RED/GREEN/
coverage cycle. Compiler-probe setup attempt `review-compile-all` failed because
of an output-path mistake and supplies no result. The provisional `-r2` used
Preview language; only `review-compile-all-final` with C# 14 is authoritative.

## Follow-up ownership

The issue table in [the tracker](../../../README.md) links all 18 new items.
Priorities and criteria separate confirmed test/coverage gaps from unresolved
contract/reachability questions. Creating these issues does not reopen or erase
the independently accepted original ten-ticket evidence. No production bug fix
or increased mutation score is claimed by this review.

[manifest.json](manifest.json) verifies every member of the compact raw evidence
archive. Reports, terminal/cleanup receipts, source/configuration identities,
compiler inputs/results and direct-check logs/TRX are retained. Both full-run
containers and every direct-check container were removed by exact owned ID;
live inspection finds no task-owned container. Reusable pinned images and shared
caches, the pre-existing stash and unrelated container remain preserved. Actual
reclaimed bytes are unmeasured where the cleanup receipt's field is null; the
pre-removal writable-layer sizes remain in terminal receipts.

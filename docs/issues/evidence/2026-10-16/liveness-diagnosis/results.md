# TT-012 bounded liveness diagnosis

Final source verification passed; independent completion review is recorded in
`completion.json` after its verdict. GitHub owns live workflow status.

## Scope and original status

The original Stryker.NET 5.0.0 reports at production snapshot `29e974a` contain
111 Timeout entries: 108 EFCore and three Benchmarks. A generated timeout is
neither a confirmed production defect nor a proved assertion kill. The original
reports and their status fields remain unchanged.

The seven broad mechanism groups are not replay equivalence classes. This
investigation applies every exact reported replacement independently against
the recorded pre-retry-guard snapshot, with source locations verified against
the original report. The later guard has its own precise fault/control evidence.

## Initial repair

The capacity monitor now waits for startup admission with its shutdown token.
Stopping can therefore settle a missing startup signal without abandoning any
claimed reader or tick. `JoinAsync` still joins both actual work and cancellation.

The original named reader test preserves all assertions and actual teardown,
with optional durable phase markers. A new deterministic held-reader test proves
that stopping cannot complete the join before a reader that ignores cancellation
actually returns. Its finally releases the gate and joins actual Completion even
when the calibration fault deliberately makes JoinAsync return early.

The same removed-start-signal fault previously reached an observation
TimeoutException, entered teardown and exceeded the external 10 second test
deadline (exit124). After the repair it retains the named TimeoutException as a
failed test (exit1) and records actual join settlement before process exit.
The separate premature-join fault fails the held-reader ownership assertion;
unmodified controls pass. None is labelled an original production defect.

## Diagnostic execution

The one-off batch is an investigation, not a maintained mutation scheduler or
a replacement score. One exact fault runs at a time in a private Docker overlay2
layer backed by immutable image135233. Layers share prepared dependencies;
host production/test files are never mutated. Network access and Docker socket
access are unavailable inside replays. Every attempt retains exact source,
commands, hashes, full logs/TRX, terminal namespace identity and exact cleanup.

Each test process has a fixed external 10 second deadline (TERM then three
seconds to KILL); VSTest's five second inactivity diagnostic may abort earlier.
Build has a separate timestamped phase; a 90 second outer supervisor and fixed
CPU/memory/PID/disk guards bound the entire attempt. These controls are diagnostic
bounds, not a claim that a deadline proves application work completed.

Original coveredBy links select source-resolved fast method identities, including
all theory rows for each selected method. Every original link resolves. A selected
pass triggers a full-fast fallback. A nonreproduction conclusion requires exact
268-row baseline membership, every row Passed, completed TRX/counters and no
unfinished sequence entry. Compiler errors, skipped/empty/incomplete selections,
inactivity aborts, deadlines and named assertions remain distinct observations.

An assertion failure and later aborted test can coexist in one run; final
analysis retains both rather than collapsing them into a kill. Five second
inactivity and external 10 second deadline are also separate dimensions.

Namespace termination/removal proves bounded owned-process cleanup. It does not
prove application resource settlement. The monitor phase logs, explicit joins,
and deterministic owned-reader/tick assertions support that stronger claim only
for their named scenarios. Original statuses remain Timeout throughout.

The ordinary native suite runs separately with real PostgreSQL/Docker contracts;
it is never selected into the diagnostic mutation replay. Concurrent native load
is disclosed; no runtime or score improvement is inferred from these timings.

## Measurement and limits

Initial monitor owning build:1.835s; coverage command:4.500s; all four monitor
tests pass. The unchanged Docker full-fast control passes268 rows in5.314s.
Per-fault build/execution timings are retained independently. No timeout,
concurrency, assertion or warning policy is relaxed to improve a score.

The original Stryker instrumentation/compiler-rollback/test-mixing environment
differs from direct single-fault compilation. Standalone compiler errors and
nonreproduction are precise limits of this diagnostic; they do not rewrite
historical Stryker statuses. TT-011 owns compiler rollback. Subsequent lifecycle
tickets retain their production-invariant scope; this investigation does not
claim to repair every generated broken state machine or synchronous infinite
loop. Whole-session mutation is queued for the final committed source after
the coding session and ordinary verification.

## Final diagnosis and verification

All111 exact standalone attempts have retained source/build/test/cleanup receipts.
The disjoint standalone outcome partition is:20 compiler-policy limits,
89 noncompleting attempts, one completed selected-test failure (197), and one
nonreproduction after complete selected and268-row full-fast runs (1469).
Of the noncompleting attempts,68 retain named unfinished sequence entries;
17 others retain console-reported named failures before external cutoff;
four retain an explicit runner limitation: no unfinished sequence or named
failure was emitted before cutoff (1545,1674,1675,1677). Expected and console-
reported row sets identify the exact unreported selection; no test start,
nontermination or settlement is inferred. Named failures occur in27 standalone
cases overall, overlapping the noncompletion categories. The per-case analysis
and raw logs, including console byte offsets, are in `exact-analysis.json`.

The retry fake's old over-budget exception was swallowed by the owner. The
maintained test now returns a fifth candidate, asserts the independent four-
attempt contract first, and returns any unexpectedly successful reservation in
finally before owner disposal. Original terminal diagnostic, distinct deletion,
empty ledger and repeated-acquisition assertions remain. Exact attempt--1445
fails Expected4Actual5 in0.349s body time; independently logged identities show
all5 resources deleted and the actual await-using scope settled. The unchanged
control passes in0.252s with exactly4 resources deleted.

Provider1962/1963 each reaches exactly one intercepted connection-open attempt
before network access; the unchanged control reaches zero. Each named failure
is reported only after actual factory/source cleanup, reserved0/deleted1.

Construction2108/2109/2126 each fails strict standalone CS8604 compilation.
That limitation remains. A separate44.836s pinned-Stryker5 diagnostic uses the
same constructor probe in Debug, no maintained warning-policy change, and
matches each exact original location/replacement to Killed by the sole test.
Each hashed native host trace binds its single active mutant ID to the named
NullReferenceException in Diagnostic/constructor (2126 via FormatMessage).
The source control passes; no application resources are allocated by this probe.
This narrow-file diagnostic's survivors/uncovered/compiler errors remain in its
report; its score is not a whole-suite result. Full mutation remains pending.

All13 final owning monitor/recovery checks pass under coverage collection.
Coverage executes the changed production await. Earlier narrow monitor coverage
leaves existing lines52 and93 unexecuted; no whole-monitor100% claim is made.
The direct fault probes separately exercise cancellation/cleanup and the guard.
Production refactor: keep cancellation at the existing monitor admission point,
without a new public seam or ownership abstraction. Test refactor: retain the
private optional phase writer; make the existing recovery fake finitely settle
and keep every original contract assertion. No general tracing framework or
suite-wide rewrite is introduced.

The first supplemental diagnostic script stopped honestly on the strict CS8604
build before runtime. Its failure and exact cleanup are retained. The corrected
attempt records those compiler outcomes explicitly; the native tool replay then
supplies actual construction runtime evidence. Earlier BuildKit/local-image and
one-off output-path setup failures remain distinguishable from test failures.

Final ordinary gate: **268 fast +167 native =435 passed**, no failures/skips, exact discovery/execution partition. Wall time:801.840s. Builds pass without warnings/errors. Before/after labelled-container inventories are empty. The earlier pre-guard435-row checkpoint remains separate historical evidence.

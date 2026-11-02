# TT-037 — Prepared background returns

The authoritative work item is [GitHub issue 35](https://github.com/DanMarshall909/TurboTest/issues/35).
This record retains implementation and investigation evidence; the issue owns live delivery status.

Prepared lease disposal now closes all registered contexts and hands reset ownership to the fixture.
A test can finish while that reset is still running; another verified Ready database can serve the
next test. A dirty database remains unavailable. Ordinary factories retain awaited reset behavior.

`TtOptions.ResetFailurePolicy` is captured per prepared fixture:

- `StopFixture` is the default. The first material reset failure rejects queued and later acquisitions
  before compensating cleanup can reserve a replacement.
- `RecoverAndReport` preserves the existing bounded recovery. A replacement must verify before reuse.

Both retain reset failures for shutdown, even when acquisition already exposed a failure. Context
cleanup failures remain immediate. Shutdown cancels slot lifecycle work, joins complete background
return wrappers, and deletes the host after successful physical cleanup before reporting reset faults.
A drain deadline only ends observation; the cleanup capability retains actual work and late errors.
No reset SQL, baseline verification, constraint, resource-limit or recovery-attempt algorithm changed.
Dynamic resource balancing and new reset backends remain outside this slice.

## Source and evidence

Base/upstream: `origin/master` at `6e4cce0868d5a1efd85234296a99a4c11c83724c`.
Implementation checkpoint: `3c03d482984c688ab70cda480c9d4c9d07b9aadd`, tree
`b27df8adee26681fb9c8355aa39c0d2914ae3d55`. All 156 compiler/test/configuration source hashes in the
successful full verification match this checkpoint. The review candidate adds only this evidence
record; its exact commit/tree and independent verdict are recorded in the work item.

[validation.json](validation.json) contains the structured outcomes and comparison samples.
[raw-evidence.tar.gz](raw-evidence.tar.gz) retains every attempt, phase command/exit/deadline receipt,
TRX, source/runtime binding, coverage report, original comparison runtimes, and one-off measurement
and diagnostic scripts. [raw-manifest.json](raw-manifest.json) names and hashes every member;
[archive.json](archive.json) records the archive digest and complete member verification.
Original working paths are under `artifacts/tt037/`; archive paths omit that prefix.

| Criterion | Behavior evidence |
|---|---|
| AC1 | Native held-reset test: context and secondary-context disposal, prompt/repeated lease handoff, separate clean database, exhausted capacity waits, shutdown joins reset. Existing one-slot restoration tests independently verify clean reuse. |
| AC2 | Native default/StopFixture/RecoverAndReport cases with held deletion, queued/later demand, caller mutation and redacted shutdown error; fast invalid-value validation before image work and policy/recovery ownership laws. |
| AC3 | Fast reentrant ownership, synchronous/delayed/multiple faults, owner cancellation while waiting and executing, foreign-token cancellation retained; native drain deadline with live backend and late material failure. |
| AC4 | Source-bound complete lanes, targeted coverage, affected maintenance/resource/DI tests updated to observable completion barriers, README and benchmark timing definitions updated. |
| AC5 | Fixed three-pair comparison below; exact-candidate independent review and hosted checks are publication gates recorded in the issue. |

RED `red-background/`: the new native test failed at the expected early-return wait against unchanged
production. RED `red-policy/`: default and explicit stop policies failed to reject demand; invalid
policy reached image preparation. The original RED test files and relevant pre-enforcement production
sources were reconstructed and checked against their retained source hashes. Build receipts and TRX
identify actual behavior failures, not compilation failures. The first RED binding was captured before
its build, so its compiled-input field describes the preceding build; the source/build/TRX evidence
is the RED provenance, not an asserted identity for its resulting test binary.

GREEN/COVERAGE/REFACTOR: 19 initial native/adjacent tests passed; 3 fast ownership laws passed; all
33 policy/configuration/ownership cases then passed with coverage. The first affected-test pass was
31/31. The production refactor keeps generic lease cleanup and ordinary returns unchanged, puts
complete-operation ownership in one small ledger, and reuses existing safe exception formatting.
Tests share a bounded condition wait for actual readiness, preserving seed/sequence, capacity,
connection and cleanup assertions. A proposed ledger-first drain was rejected during read-only
review because resets can wait behind maintenance that needs shutdown cancellation.

The first full candidate attempt passed 373 fast and 176/181 native tests. Five old completion
assumptions failed: benchmark preflight capacity, preparation/administrator/ambient-transaction
counts, and DI reset logging. Their original sources and failure receipts remain in `final/`.
The repaired benchmark settles physical returns before advancing preflight and before measured
phase wall-time completion; recreation holds the same cleanup permit until verification finishes.
It does not claim all adaptive preparation or outer logging is settled at that phase boundary.
Seven focused repair cases passed before the final full run.

## Verification and limitations

The final uninstrumented command was:

```sh
DOCKER_HOST=unix:///var/run/docker.sock python3 scripts/verify.py all --output artifacts/tt037/final-repaired
```

It passed **554/554 tests: 373 fast and 181 native, zero failures/skips**. Fast process wall was
5.013s; native process wall was 879.778s. Source inputs were unchanged, all owned process groups
stopped, and the labelled Docker inventory was unchanged. No deadline or assertion was weakened.
The seven new fast ownership cases totalled 0.029s, maximum 0.0066s in this run. Native boundary tests
remain in the slow lane; no lane was moved on the basis of one timing sample.

The pre-change baseline was **not green**: 365 fast and 175/176 native passed, with one maintenance-drain
timeout. Both isolated retries passed; that test also passed both full candidate runs. Its cause was
not established. Keep this failure when interpreting baseline timing.

Final coverage used the installed Microsoft.CodeCoverage collector. All 373 fast tests passed;
all 26 instrumented lines of the new background-return ledger executed in that fast run. Coverage
shows execution, while the held-work/fault assertions establish the associated contracts. Existing
slot/prepared-pool coverage is partial; no whole-module 100% claim is made.

The first owning native coverage attempt passed 12/13 cases and recorded a `PreparedPoolDispose`
shutdown failure in the fixed-eight-slot benchmark case. Its resource inventory still settled.
An isolated instrumented retry of all three benchmark configurations passed. A temporary startup
hook recorded only TurboTest's already-redacted lifecycle messages; it loaded successfully and
observed no lifecycle exceptions in that retry. Source/runtime inputs were unchanged. The first
failure remains unexplained and retained; the entire first instrumented run is not called passing.
The ordinary complete correctness gate above passed without instrumentation. This coverage scope
is the full fast lane plus selected owning native cases, not complete slow-lane attribution.

Post-session mutation is **pending**, not passing: the local reflink probe returned `EOPNOTSUPP`, no
safe CoW mutation sandbox has been provisioned, and the full ordinary-lane source-section exclusion
adapter has not been calibrated. Do not substitute full-copy/shared-write sandboxes or send native
end-to-end tests to mutation. These are disclosed follow-up prerequisites, not ordinary delivery gates.

## Fixed comparison

Three alternating pairs ran the same six unchanged normal-use integration methods from SeededBaseline,
BaselineRestore, MigratedBaseline and PreparedCapacity tests. Method hashes and relevant unchanged
helpers/runner configuration were checked against the baseline. Both retained runtimes used the
same VSTest invocation, xUnit parallelism of two, native Linux Docker daemon, and cached PostgreSQL
16 image `sha256:81bd698b4594e751a3269e4dcd3e03a4a0ec0daf7b72e7aa1abd43cce9887542` (16.15).
The contract was written before timing; no warmups, result-driven extra rounds or discarded attempts.

| Pair | Awaited baseline wall (s) | Background candidate wall (s) | Candidate / baseline |
|---|---:|---:|---:|
| 1 | 57.073 | 31.894 | 0.559 |
| 2 | 49.439 | 73.756 | 1.492 |
| 3 | 27.073 | 24.568 | 0.907 |

All 36 test executions passed and both runtimes remained byte-identical throughout measurement.
Total wall was 133.586s baseline versus 130.218s candidate (ratio 0.975). Pair ratios ranged from
0.559 to 1.492; median 0.907. **These noisy three pairs establish no consistent wall-time speedup.**
They include process startup, test setup and complete fixture disposal. Summed per-test TRX durations
are retained separately and are not wall time under parallel execution. Container and volume
inventories were unchanged after the comparison.

Most selected fixtures use one slot, so the next acquisition still has to wait for reset. This is not
a sustained spare-pool throughput benchmark. Shutdown may skip queued terminal resets while still
joining accepted work and deleting resources, so reset counts must not be assumed identical merely
from endpoint wall time. The full before/after suites also differ in test count and changed lifecycle
assertions and are not a controlled performance contrast. TT-036's synthetic overlap result remains
separate evidence; no general speedup, Respawn equivalence, 50ms promise or Windows result is inferred.

## PostgreSQL 18 proposal

The suggested clone path is real. PostgreSQL 18 documents `file_copy_method=clone`, using
`copy_file_range()` on Linux/FreeBSD and `copyfile()` on macOS to let supported systems share blocks.
This is an opportunity for storage offload, not a guarantee based only on a filesystem name.
[Official resource settings](https://www.postgresql.org/docs/18/runtime-config-resource.html#GUC-FILE-COPY-METHOD).

`CREATE DATABASE ... STRATEGY=FILE_COPY` still forces checkpoints before and after creation; the
ordinary WAL_LOG strategy is normally more efficient for small templates. Thus a reported 200ms
clone is a useful author measurement, not a portable reset-time bound.
[Official CREATE DATABASE](https://www.postgresql.org/docs/18/sql-createdatabase.html).

Consistent filesystem snapshots must include the complete cluster and WAL (and simultaneous
snapshots across relevant volumes); restoration of a running-server snapshot requires crash
recovery. Snapshot metadata speed excludes shutdown, recovery and reconnect costs.
[Official filesystem-backup guidance](https://www.postgresql.org/docs/18/backup-file.html).
The cited storage-copy article does not by itself establish that CRIU invariably corrupts PostgreSQL.
Cloud branch or schema-isolation latency figures were not independently established in this slice.
PG18 cloning could replenish the same prepared pool later; no PG18/backend migration was performed.

## Review and publication envelope

Review the complete diff from the base to the candidate containing this report, the retained raw
archive/manifest, all failures and the qualifications above. No unrelated work is included.
After exact-candidate independent GPT-6 Astra PASS, the allowed envelope is: verify unchanged tree,
fetch without integration and require the same upstream object and absent destination branch;
normal push to `origin/work/tt037-background-returns`; create the PR; require hosted correctness/pack
checks and branch protections; normal merge with unchanged reviewed head/base; update the issue and
Project15 from verified outcomes; return the clean primary checkout to master and remove only the
fully merged local feature branch. No post-review semantic changes or release publication are included.

Raw evidence and two reusable comparison runtimes are retained within the existing 2GiB evidence
budget. The reflink probe removed its own temporary files. Shared caches and unrelated containers
were preserved. Owner understanding remains unconfirmed until handover; that does not reopen
completed engineering gates or authorize a new goal.

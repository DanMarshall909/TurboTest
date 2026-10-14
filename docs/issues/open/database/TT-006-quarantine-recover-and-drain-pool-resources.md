---
id: TT-006
title: Quarantine recover cancel and drain pool resources
status: Open
priority: P1
type: Bug
created: 2026-10-14
depends_on: [TT-005]
---

# TT-006: Quarantine, recover, cancel and drain pool resources

## Problem

Reset failure currently destroys a container without replenishing usable capacity. The prototype also exposed a recovery ordering bug: it briefly marked a replacement Ready before validation. Production ownership must remain correct through failures, cancellations and shutdown.

Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md), including the named replacement-validation RED/GREEN probe.

## Scope

Add recovery and drain behavior to the bounded owner. Keep dirty/uncertain resources quarantined, track all lifecycle work and expose terminal failure when recovery cannot restore service. Use bounded retries/backoff rather than indefinite creation loops.

## Acceptance criteria

- [ ] Reset or validation failure quarantines the slot; it cannot be leased while a replacement is being created or validated.
- [ ] Recovery verifies the replacement's schema, seed, sequences and identity before publication. A corrupt replacement remains unavailable and retains correct accounting.
- [ ] Creation/reset/drop failure and competing recovery attempts cannot exceed the cap or lose a reservation. Unconfirmed deletion stays accounted until cleanup succeeds or the owner reports a terminal failure.
- [ ] Queue cancellation removes only that request and cannot lose a slot. Cancellation after acquisition/while preparing/resetting has a documented cleanup owner.
- [ ] Shutdown stops new acquisitions/controller work, settles queued requests, drains leases and lifecycle work under a documented timeout policy, and releases task-owned sources/databases/containers without touching unrelated resources.
- [ ] Disposing twice, concurrent return/shutdown and failed disposal are observable, bounded and idempotent; callback errors are not silently reported as successful cleanup.
- [ ] After a recoverable failure, effective capacity is restored. After terminal failure, waiting callers receive an actionable error rather than hanging indefinitely.

## Verification

Inject one failure at a time: partial reset/sequence change, post-commit bad seed, invalid replacement, clone failure, failed deletion, canceled queued acquisition and shutdown during recovery. Assert the named diagnostic, non-publication, accounting and eventual drain/recovery. Retain real PostgreSQL failure-boundary tests.

## Implementation log

- **2026-10-14 — partial groundwork:** A broad test attempt repeatedly failed
  during PostgreSQL initialization and left 29 task-owned containers behind.
  The run was stopped; its processes were verified terminal, logs/identities
  were retained and the containers/anonymous volumes were removed. Creation
  now validates the data source before Docker creation and records the
  container before starting/awaiting readiness. Failed preparation deletes
  that resource, and disposal enumerates all owned containers. A regression
  using a missing database rejected the old cleanup path and now independently
  observes zero remaining containers. See the
  [evidence manifest](../../evidence/2026-10-14/pooled-reset/manifest.json).
  Bounded reservations, retries, terminal recovery, true startup deadlines,
  tracked concurrent lifecycle work and lease-aware drain remain open; this
  change does not resolve TT-006.
- **2026-10-14 — startup gate investigation:** The migrated full suite passed
  30 checks, but the final 31-check rerun then passed 25 and failed six during
  PostgreSQL startup. Failed resources were removed; no TurboTest containers
  remained. Sanitized Docker events show seven resources reaching cleanup
  about 30–33 seconds after start and subsequently requiring forced termination.
  This suggests unfinished startup but does not identify its phase. A separate
  readiness flaw also exists: a failed query after successful connection open
  leaves the retry loop reusing an open connection. A bounded diagnostic batch
  now captures timestamped PostgreSQL logs before cleanup. Neither a timeout
  increase nor a clean completion gate is inferred from the earlier pass.
- The diagnostic batch reproduced three startup failures and retained four
  containers' timestamped logs: each stopped after post-bootstrap initialization
  without completing `initdb`. Disposable clusters now use the PostgreSQL
  testing option `--no-sync` for initial creation. A real check verifies that
  server `fsync` remains `on`; the three failed workload checks plus that check
  pass together. README states the crash/recreation contract. This does not
  disable server durability settings or establish a startup latency guarantee.
- A separate real PostgreSQL fault makes the first readiness query fail after
  opening its connection. The former loop times out instead of retrying; each
  attempt now closes its connection wrapper and the next query succeeds. Both
  RED/GREEN results are retained. True deadline/cancellation and controller
  recovery remain under the acceptance criteria above.

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

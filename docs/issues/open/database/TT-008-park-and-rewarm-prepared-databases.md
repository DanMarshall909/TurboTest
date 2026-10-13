---
id: TT-008
title: Park and rewarm prepared databases
status: Open
priority: P2
type: Feature
created: 2026-10-14
depends_on: [TT-005, TT-006]
---

# TT-008: Park and rewarm prepared databases

## Problem

Dropping an idle database took roughly 4–5 seconds in several probes. Parking a verified database by closing its data source took a median 8.53 ms; recreating the source and validating it took 36.37 ms. Parking retains files and defers deletion cost: it did not improve total runtime in the measured short workload.

Evidence: [TT-000](../../resolved/database/TT-000-pool-feasibility-investigation.md).

## Scope

Separate active warm capacity from retained prepared capacity. Provide a parking/rewarm lifecycle plus configurable extended idle deletion/fixture shutdown. Initially use explicit/fixed policy so correctness can be verified independently of TT-009.

## Acceptance criteria

- [ ] Only an idle verified Ready slot can be parked; no leased/resetting/preparing slot can be selected.
- [ ] Parking closes that slot's owned connection pool while retaining its known baseline database and physical reservation. Closing sessions does not imply database files or all server memory were freed.
- [ ] Rewarm prefers a retained compatible slot over a fresh clone, including when reservations already equal the hard cap.
- [ ] Rewarm validates baseline, schema and identity before Ready publication. Corrupt seed/schema and failed connection opening remain quarantined and follow TT-006 recovery.
- [ ] A bounded warm floor, retained-database cap and idle deletion policy are configurable and validated. Expiry removes only idle slots and releases reservations only after confirmed deletion.
- [ ] Rewarm/retirement concurrency is bounded; a renewed request cannot race a physical deletion into double ownership.
- [ ] Fixture shutdown drains and deletes parked as well as active databases. Deletion/teardown costs remain visible in metrics.

## Verification

Test park → idle → renewed burst with actual resource identity reuse and closed-session observations. Use a controllable clock for expiry/races and real PostgreSQL checks for seed/schema corruption and failed rewarm. Do not treat per-database rewarm time as whole-burst checkout latency.

# Adaptive capacity comparison

This local Release experiment compares equal physical/connection/resource caps and an equal startup-two fixed/adaptive pair, with an already-warm fixed-eight control. Two seeded counterbalanced blocks use identical per-block workload plans and a shared 15-second idle. All six cases and 954 cycles pass; each case has 53 samples per row size (130/1,300/13,000), complete cleanup and no failures. The command took 308.19 seconds including build/startup and cleanup.

Raw receipts, source snapshots, phase samples/events and independent analysis are retained in the [evidence manifest](manifest.json). The source cut is the uncommitted candidate over `b5694c8243462c84579d7a8da30c92d712aa4577`; this is a local observation, not a portable performance guarantee.

| Block | Profile | First-burst checkout p95 ms | Renewed-burst checkout p95 ms | Repeated-burst checkout p95 ms | Heavy cycles/s | Known databases | Idle target / parked |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 1 | fixed-8-startup-2 | 460.02 | 0.47 | 0.28 | 7.32 | 8 | 8 / 0 |
| 1 | adaptive-8-startup-2 | 1441.24 | 1058.98 | 969.67 | 6.22 | 3 | 2 / 1 |
| 1 | fixed-8-startup-8 | 0.28 | 0.28 | 0.29 | 7.33 | 8 | 8 / 0 |
| 2 | fixed-8-startup-8 | 0.26 | 0.28 | 0.23 | 6.91 | 8 | 8 / 0 |
| 2 | adaptive-8-startup-2 | 1208.14 | 1020.33 | 741.00 | 7.35 | 4 | 2 / 1 |
| 2 | fixed-8-startup-2 | 825.72 | 0.27 | 0.27 | 7.16 | 8 | 8 / 0 |

Fixed mode wins burst checkout in both blocks. Adaptive first-burst p95 is 1,208–1,441 ms versus 460–826 ms for matched startup-two fixed capacity, and 0.26–0.28 ms for already-warm fixed eight. Heavy throughput is mixed: adaptive 6.22/7.35 cycles/s versus matched fixed 7.32/7.16. Total case duration is not a substitute for those phase-specific outcomes. Startup ordering, caches, JIT and shared-host noise remain limitations despite paired reversal. Two blocks do not establish statistical significance.

Adaptive grows 2→3 in each first burst, parks 3→2 during idle, and rewarms 2→3 on renewed demand. The second block reaches four during heavy load. Reset saturation vetoes further growth repeatedly; queue pressure alone therefore does not imply useful extra capacity. The separate public native 1→8→1→idle→8 regression proves eventual bounded growth, eligible parking, retained OID reuse, restored seeds/sequences and final drain under held demand. The performance workload is allowed to produce a different target.

All profiles use a 256 MiB host, preparation/reset concurrency one, four connections per workload source and two administrator connections. Sampled raw cgroup peaks are approximately 255–256 MiB in every case and include cache. Process peaks rise across the shared benchmark process (approximately 175–233 MiB). These observations do not prove per-mode resident-memory savings or worktree/build reserve requirements. Adaptation demonstrably owns fewer workload databases here; that is the supported resource-count conclusion.

## Default selection

- Adaptive mode remains opt-in. Fixed mode is the default because short and renewed bursts can be substantially faster when warm capacity is already available.
- The one-second growth window requires sustained pressure rather than mirroring the 100/200/750 ms prototype timers. Earlier fixed comparisons show substantial subsecond-to-second queue costs at small capacities; this window deliberately accepts ramp-up cost to avoid reacting to brief spikes. The new run confirms that tradeoff, rather than showing a latency improvement.
- The ten-second spare window exceeds the earlier two-second fixed/parking lull. The new shared 15-second idle produces one eligible shrink and retained rewarm, while short intervening phases do not repeatedly shrink. Each step restarts the window; eight-to-two takes at least six windows plus checks/work.
- The two-second shared cooldown separates opposing changes. A one-second check interval and three-second maximum reading age give bounded freshness tolerance. The 80-percent working-set growth ceiling reserves a 20-percent headroom margin. These are conservative policy choices with deterministic boundary tests and actual owned-host sampling calibration, not experimentally optimal thresholds.
- Default minimum/startup is one and maximum is the physical cap. Workloads can specify a startup/minimum hint, as this comparison does with two. Invalid limits, timer ranges, age and Ready-floor combinations fail before allocation.

No window sweep or cross-machine optimization was performed. CPU is omitted. Missing/invalid/stale memory blocks growth while preserving the target and allowing eligible shrink. Resource and reset-saturation vetoes, explicit target commitments and joined drain are correctness constraints, not claims that every rejected scale-up would reduce throughput.

Independent reconstruction verifies 1,680 copied source snapshots, common compiler-input/assembly/image identities across the six cases, equivalent per-block plans, reversed profile order, startup 2/2/8, unique bounded database/OID identities, exact sample counts, all phase throughputs and 225 percentile summaries. Final current-source whole-suite/build validation and independent completion review are recorded separately in the manifest.

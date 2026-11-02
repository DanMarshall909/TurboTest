# TT-034 Docker memory and network controls

Dan accepted this follow-up after asking whether Docker itself caused the poor
performance. [Protocol](../../../../../experiments/tt034/DOCKER.md),
[summary](docker-summary.json), [validation](docker-validation.json).

**Neither control demonstrated a clear improvement.** E-Maj remained slower in
all 15 pairs. These results weaken the memory-limit/bridge-network explanations;
they do not quantify Docker's total overhead against native PostgreSQL.

Five independent rounds per setup; each contender performs 100 complete sequential
cycles. All C# executable bytes and workload sources match the original screen.
Only the driver changes infrastructure and captures diagnostics outside SuiteMs.

| Setup | Respawn median | E-Maj median | E-Maj/Respawn |
| --- | ---: | ---: | ---: |
| Bridge, 256 MiB | 2.168 s | 6.491 s | 2.994 |
| Bridge, 1 GiB | 1.259 s | 6.232 s | 4.949 |
| Host networking, 1 GiB | 1.268 s | 6.150 s | 4.851 |

| Change | Contender | Alternative/reference ratio | Paired 95% interval |
| --- | --- | ---: | --- |
| 256 MiB to 1 GiB, bridge | Respawn | 0.581 | [0.400, 1.202] |
| 256 MiB to 1 GiB, bridge | E-Maj | 0.960 | [0.790, 1.232] |
| Bridge to host, 1 GiB | Respawn | 1.007 | [0.514, 2.160] |
| Bridge to host, 1 GiB | E-Maj | 0.987 | [0.792, 1.317] |

All intervals include 1. Respawn's larger-memory median is notably lower, but
five pairs with substantial variability do not establish a repeatable improvement.
The predeclared criterion (upper 95% ratio below 1) was not met. Intervals enumerate
all 3,125 paired bootstrap samples; their breadth does not prove equivalence.

## Memory observations and limits

- Highest container-lifetime memory peak: **150.082 MiB**, below the smaller cap.
  Peaks include startup; subtracting peaks does not yield an interval peak.
- All observed current/peak swap values and memory.events deltas were zero.
- Two 256 MiB cases recorded small pressure deltas: 640 microseconds for Respawn
  and 146 microseconds for E-Maj. Each appears in both some/full counters; do not
  add those overlapping values. All other pressure deltas were zero.
- Counter intervals include application preparation, warmup, measurement and
  diagnostic commands, not just SuiteMs. They cannot assign pressure to a query.
- Docker's default swap allowance grows with its RAM cap when host swap exists;
  this host has zram. This is a **RAM-plus-default-swap budget** comparison.
  Per-case swap limits were not captured; actual observed swap use was zero.
  This qualification was identified while the frozen run executed. Inputs were
  not changed mid-run and no invented limit measurements were added.

Host networking used a selected port and verified 127.0.0.1-only PostgreSQL
binding. Its comparison keeps the same 1 GiB budget, volume, SQL, assertions and
relaxed durability. No host firewall, filesystem, swap or kernel changes occurred.
No native PostgreSQL installation/comparison was performed.

## Evidence and disposition

Measured checkpoint: `2dcf0663559b94e9b00e9617bacf47b19477bc57`.
Original image/release/dependency identities are in the manifests. Both smoke
cases and all 30 measured cases passed; each has 100 ordered samples, ordinary
assertions, final audit, settings checks and source/executable binding.
An independent read-only recomputation matched both statistics and their intervals.

All 32 containers/volumes from this phase were removed with verified absence.
Together with the first phase, **77 owned containers and 77 volumes** were removed.
Final inventory retained the two unrelated pre-existing containers. There were no
failed performance cases or retries in this follow-up.

[resource-inventory.json](resource-inventory.json) retains a later direct absence
check of all 77 container and volume IDs. It distinguishes that fresh observation
from the initial inventory, which was observed only in session tool output.

[docker-evidence.tar.gz](docker-evidence.tar.gz) has 164 verified members, SHA256
`713b593919fd5ea2426ea65bd658b4a800f93ca471cb272c8a4f554605024261`.
No production code, maintained tests or workflows changed. The Docker analyzer
was added after execution began and was not an execution input.

**Decision:** stop these controls. Docker is not established as the cause of
E-Maj's disadvantage. A matched native run would be needed to measure its remaining
effect; no such result is claimed here.

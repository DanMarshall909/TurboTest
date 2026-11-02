# TT-034 Docker controls

Dan accepted these controls after asking whether Docker itself explains the
performance on 2026-11-02. The original 40-case screen remains frozen at a822c75
and its negative result is preserved. These follow-up measurements address the
memory/network setup, without installing native PostgreSQL or a new backend.

Keep the same compiled C# executable, SQL, assertions, qualification, five warmup
cycles, 100 measured cycles and complete synchronous-reset timing boundary. Use
the relaxed PostgreSQL settings throughout. Five independent paired rounds of:

1. Bridge network, 256 MiB (fresh contemporaneous control).
2. Bridge network, 1 GiB (memory comparison).
3. Host network, 1 GiB (network comparison against #2).

Each configuration runs both actual Respawn and E-Maj. Rotate configuration order
between rounds; alternate contender order. There are 30 measured containers/3000
cycles, plus two host-network smoke containers if successful. Correctness/setup
repairs are retained separately and cannot be credited as performance successes.
Host-network PostgreSQL listens only on 127.0.0.1 at a freshly selected port;
binding races fail visibly instead of taking over another service.

Record cgroup-v2 memory.events, current/peak memory, current/peak swap and pressure
before/after the application process. These cover preparation, warmup and measured
work together, not just SuiteMs. Keep container preparation separate. Do not change
the host's swap settings, kernel, firewall or filesystem. Memory budgets remain
bounded; no host installation or unrestricted-memory variant.

Compute contender comparisons and same-round configuration/reference ratios with
exact paired bootstrap intervals. A clear improvement means the upper 95% interval
for that configuration/reference ratio is below 1; a large material effect is
also reported in absolute milliseconds. No new 2x claim or four-worker scalability
claim. Even a negative result cannot establish zero Docker overhead: a matched
native PostgreSQL run would still be needed for that. Stop after these controls
with a bounded conclusion; no speculative optimization sweep.

Run `python3 experiments/tt034/run.py --docker-controls --smoke --output artifacts/tt034/docker-smoke-01`,
then `python3 experiments/tt034/run.py --docker-controls --output artifacts/tt034/docker-screen-01`.

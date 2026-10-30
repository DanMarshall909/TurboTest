# Correctness verification

From the repository root, run the maintained driver with a new evidence directory:

```bash
python3 -m unittest discover -s scripts -p test_verify.py
python3 scripts/verify.py fast --output artifacts/verification/fast-001
python3 scripts/verify.py slow --output artifacts/verification/slow-001
python3 scripts/verify.py all --output artifacts/verification/all-001
```

`fast` selects `Category!=PostgreSQL`; `slow` selects `Category=PostgreSQL`.
`all` runs both lanes separately. Every invocation builds Release, builds the
supported DynamicScalingExample outside the solution, and discovers all three
selections. The catalog retains every parameterized display row. Duplicate rows,
overlap, missing discovery, missing execution results, failures and skips fail
verification. VSTest discovery diagnostics retain executor test IDs. TRX retains
the same IDs and per-test timing; the catalog binds those results to the exact discovered rows. A timeout never counts as a pass.

The driver requires Linux, Python 3.10+, Git, the SDK selected by `global.json`,
and restored NuGet dependencies. Native tests require Docker Engine at
`unix:///var/run/docker.sock` and a locally available `postgres:16-alpine` image:

```bash
docker --host unix:///var/run/docker.sock version
docker --host unix:///var/run/docker.sock pull postgres:16-alpine
```

Image preparation and native tests use that same daemon. The driver rejects a
conflicting `DOCKER_HOST` and TLS variables instead of silently connecting to a
different daemon. An unavailable daemon or image fails before native execution.
The current native tests explicitly select this Linux socket. Windows and remote
Docker daemons are not verified by this driver. The fast lane needs no Docker.

Each phase retains its command, working directory, deadline, elapsed time, exit
code and complete log. Test processes have a 60 second inactivity deadline;
external phase deadlines are 120 seconds for fast and 900 seconds for slow.
Every phase checks that its process group settles, even after a zero exit code.
Timeout/cancellation stops only the phase's own process group and records whether
its workers have settled. CI adds a 30 minute job deadline and always uploads the
evidence, including failed TRX and blame sequence files. Deadline failures retain
their original status even when workers stop successfully. Latency benchmarks
are separate and impose no correctness threshold.

The final source identity includes product, test, script, workflow, SDK, project,
actual test-output binaries and resolved dependency inputs. The driver rejects input changes during testing.
Run native verification serially with other TurboTest activity on the same Docker
daemon. Before/after inventories retain exact labelled container IDs and names;
any change fails the settlement check. The driver does not delete an unknown
container. Boundary tests retain their database/session/container ownership and
cleanup assertions; the inventory provides an additional leak diagnostic, not
proof that every new resource on a shared daemon belongs to this run. On a killed
job inspect its exact logs and container identities before cleanup.

The source and maintained workflow are published in the
[TurboTest repository](https://github.com/DanMarshall909/TurboTest).
[Hosted CI](https://github.com/DanMarshall909/TurboTest/actions/workflows/ci.yml)
uses the same driver above. Verify the hosted run for the exact source commit;
a local pass never establishes a hosted workflow pass.

# TT-029 verification evidence

[Live issue](https://github.com/DanMarshall909/TurboTest/issues/19) owns workflow status.
The verified implementation snapshot is `4d0411b7597c75e64fd57579c19581232202479b`. The clean source worktree
restored dependencies, built the solution and DynamicScalingExample with zero
warnings/errors, and passed 267 fast plus 167 native tests without failures or
skips. Exact executor IDs and parameterized rows form a disjoint partition of
all 434 discovered tests and match the actual TRX rows.

The final receipt records 634.085 seconds wall time. Raw logs,
phase commands/exit codes/durations, SDK and Docker/image identity, per-test TRX,
source/runtime/dependency hashes and resolved assets are retained in `runs.tar.gz`.
Archive member hashes were independently reread before admission.

The normal driver rejects the deliberate conflicting Docker configuration with
`DockerConfiguration` before native execution. Ten operational contract tests
cover missing/duplicate/failed/skipped rows, malformed summaries/IDs/timing,
partition loss, deadline settlement, normal-exit orphan settlement and SIGTERM
through the production entrypoint. Retained RED/GREEN logs show their repairs.

Both container inventories are empty. Actual native ownership/session/database
cleanup assertions remain in the ordinary suite. The driver deletes no unknown
resource. Process receipts record settled phase groups, including failure and
cancellation contract cases.

The first clean attempt failed before build because a non-cone sparse checkout
omitted the root solution. Its original failure receipt is retained. Reinitializing
cone mode restored the declared source inputs; the final checkout remained clean.
The earlier full passing run is retained as historical evidence, not relabelled
as current-driver verification.

Python trace reports 83% driver line execution for the unit-plus-fast-CLI scope.
The native CLI was not traced. Native-only branches have actual combined-run
outcome evidence; unsupported platform/error paths are not claimed fully covered.
No production C# assembly changed, so this slice does not enqueue a new .NET
mutation run. Production and test refactoring retained a single driver and shared
parsers; redundant guards were removed and discovery parsing was grouped with
catalog handling. No additional abstraction was justified.

Hosted workflow execution remains pending because source publication has no
configured code remote. Linux with the local Unix Docker socket is the verified
platform. The CI definition uses the same driver and daemon, retains artifacts on
failure and imposes a 30-minute job bound. No benchmark latency is a correctness
threshold. These limitations remain visible on the live issue.

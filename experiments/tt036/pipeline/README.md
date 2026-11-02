# TT-036 ready database pool with background resets

Active isolated experiment for [TT-036](https://github.com/DanMarshall909/TurboTest/issues/33). Dan clarified that ready databases and overlapping background resets were the intended design. The earlier storage harness and eight-cell smoke qualification are preserved; its profile and performance matrix were not executed. No production implementation/readiness claim is made here.

## Contract and comparison

Every reset uses actual Respawn7.0.0 ResetAsync followed by the same224-row seed and explicit sequence restoration. Four test workers execute the same committed three-connection workload and independent observer assertions. Three modes:

- `direct`: four fixed database slots, one per test worker, synchronous reset before its next test.
- `awaited`: eight database slots, bounded ready/dirty queues, four reset workers; test return waits for that reset ticket.
- `background`: the identical eight-slot pool and reset workers; test return enqueues reset and the next acquisition can use another clean slot.

The two pool modes isolate the wait policy on an identical implementation. The direct comparison additionally includes pool coordination and doubled slot count/connection/working-set costs. Same1GiB server memory cap, explicit zero swap, bridge networking, no CPU quota, pinned PG16.15 image and ordinary logged data volume. fsync/synchronous_commit/full_page_writes are off; normal integrity/transaction/trigger settings remain. More slots are an explicit resource tradeoff, not free capacity. Preparation and observed memory are reported separately.

Only a successful complete reset can publish a slot Ready. Leases close all three application connections before return. Atomic slot transitions reject duplicate/concurrent ownership. Reset concurrency and both queues are bounded. Failed reset/application work closes ready publication/acquisition and aborts the batch; all in-flight application and reset work is joined before pool/database disposal. The injected smoke fault throws after Respawn commits TRUNCATE but before seeding: the failed slot must stay unavailable, the exception must surface, its Parents table must remain empty, and all owned tasks must stop before container removal.

Each mode adds0,5,or20ms of explicitly synthetic non-database wait to each test, while retaining its lease. This models application/external time available for overlap; it is not CPU work or evidence about a particular real test suite. With independent stage capacity, ideal per-lane throughput approaches max(test,reset) rather than test+reset; contention/queueing can prevent that gain. Largest relative theoretical gain occurs near equal stage durations, not when either stage dominates.

## Measurement boundaries

Five rounds × three delays × three modes =45cases/45,000tests. Every case has four test workers and1,000complete tests. Rotate duration/mode order; no extra result-dependent rounds. Fresh owned server per case. Qualify each slot with three committed multi-connection cycles, exact baseline/sequence restoration and FK/PK/NOT NULL rejection. Warmup20tests and exact baseline audit are outside primary timing. User triggers/CHECK behavior and arbitrary schemas are not individually qualified; no feature disabling them is introduced.

SuiteMs starts before test-worker dispatch and ends only after all test workers and reset tickets finish and original ready capacity is restored. It includes the final reset drain and in-memory ownership/capacity check. Final database baseline audit and disposal are separate. No baseline/schema query is repeated per reset. Application assertions stay inside every measured test. Per-test times retain acquisition wait, application work, return, reset queue wait, reset execution and test finish; checking successive leases against prior reset completion proves no premature reuse. The timeline is descriptive; instrumentation is identical between pooled modes.

Primary contrast: background/awaited suite medians at each delay. Background/direct and awaited/direct are secondary reference comparisons. Exact paired bootstrap over all3,125ordered five-round resamples, nearest-rank95% interval, preserving rounds. Pointwise intervals have no multiplicity adjustment; five rounds on one shared host have limited resolution. Upper bound below1 supports an improvement in this screen; below0.5 supports2× for that contrast. Lack of significance is not evidence of parity. Pooled sample medians are descriptive, not independent experiment replicates or additive suite-wall components.

Record queue high-water marks, reset concurrency, lease/queue waits, final drain and cgroup memory/CPU/I/O. Cgroup observations include setup through disposal, and peak memory is a lifetime high-water mark. Timelines can measure stage overlap/occupancy but not CPU utilization. These signals may inform a future controller balancing test workers, reset workers and ready buffer within one budget. Dynamic balancing is deferred; this experiment fixes capacities.

Raw pool queue/concurrency fields are null in direct mode, where no queue/reset-worker pool exists. Timeline-derived reset concurrency remains available for all modes. The injected slot is tagged before reset starts, so a premature ready publication would increment its failure-probe counter; the fault check also requires that exact slot to remain Failed and empty after all work is joined.

## Reproduction and evidence

```sh
dotnet build experiments/tt036/pipeline/PipelineScreen.csproj -c Release
# Copy DLL/JSON runtime files to a new retained artifacts/tt036/pipeline-runtime-<version>.
# Never overwrite bytes used by an attempt. Commit all measured inputs before measurement.
python3 experiments/tt036/pipeline/run.py --phase smoke --runtime artifacts/tt036/pipeline-runtime-<version> --output artifacts/tt036/pipeline-smoke-<attempt>
python3 experiments/tt036/pipeline/run.py --phase measure --runtime artifacts/tt036/pipeline-runtime-<version> --output artifacts/tt036/pipeline-measure
python3 experiments/tt036/pipeline/analyze.py artifacts/tt036/pipeline-measure
```

Smoke includes the nine normal mode/delay cells (12tests each) and one intentional reset failure. All other failures stop the phase for repair and remain retained. The runner reuses only lifecycle helpers from the parent TT-036 driver, whose source is also hashed. Preserve initial/final inventories, runtime bytes, raw reports and receipts. Cleanup removes only exact owned containers and their volumes after processes stop; retain builds/evidence and unrelated resources. The measured checkpoint remains in normal merge history.

# TT-022 lifecycle admission, publication and drain

This record supports [TT-022](https://github.com/DanMarshall909/TurboTest/issues/12). GitHub owns the live checklist and status. The candidate is based on `origin/master` `524c4a9d03db7f852c5a1d27acf8550397e261ea` (refreshed 2026-10-30). Historical mutation statuses remain unchanged.

## Observable contracts

| Issue outcome | Evidence |
| --- | --- |
| Shared work is published before reentrant callbacks | A shutdown cancellation callback observes the same factory drain task; a context configuration callback closes its factory before lease publication and sees the same pending drain; the slot resource factory sees its Preparing reservation before it invokes reentrant drain. |
| Canceled and late admission is rejected | A precanceled direct factory lease request never invokes its pool; precanceled static and scope requests start no owner/lease; a closed scope and factory reject late requests; the real provider rejects a queued lookup behind held startup when closure begins. |
| Admitted work remains owned through drain | The context callback holds an acquired database before lease publication; drain stays pending, then the unpublished database is discarded once. The real provider holds startup while a second lookup queues; interruption retains its exact host and explicit cleanup joins the held work. |
| Inner database reentrancy reachability | A public factory-created lease reaches the inner database callback through context disposal. Calling lease disposal again there shares the pending return; context disposal occurs once and the database returns once. This proves that particular public path, without claiming arbitrary internal callback reentrancy. |

## Verification and calibration

- The initial precanceled factory test was RED: a precanceled request reached the pool and threw its probe error. The single production change checks the caller token before admitting work. The first maintained GREEN run passed 358 fast rows.
- Removing the factory's acquisition registration from the final source made `Context_callback_can_close_factory_before_lease_publication_without_losing_acquired_database` fail at its pending-drain assertion (`Assert.False`, line 470 in that fault build). Its held callback was released in `finally`. The registration was restored before final verification. Fault log: `artifacts/verification/tt022-held-acquisition-fault/test-fast.log` (SHA-256 `673ea24061546cb6138e9ac34c0835f3a4b03de6e175e9fd9b2e1e46f81317f4`).
- Supplemental fast coverage: `dotnet test tests/TurboTest.Tests/TurboTest.Tests.csproj --no-restore -c Release --filter 'Category!=PostgreSQL' --collect 'Code Coverage;Format=cobertura' --logger 'trx;LogFileName=fast-coverage.trx' --results-directory artifacts/verification/tt022-fast-coverage` passed 365/365. The changed `TtFactory.CreateLeaseAsync` line executed. Coverage is execution evidence, not an assertion score.
- Final canonical command: `python3 scripts/verify.py all --output artifacts/verification/tt022-final-all` passed 365 fast and 176 PostgreSQL rows, zero failures/skips, in 850.77 seconds. Solution and separate example builds passed. Before/after Docker resource IDs matched; no automatic deletion was needed. The retained [verification receipt](verification.json) and [source identity](source-identity.json) bind this run to its inputs. Full logs/TRX remain in the named local artifact directory.
- `python3 -m unittest discover -s scripts -p test_verify.py` passed 10 tests; `git diff --check` passed.

## Limits and next gate

The new tests prove named, deterministic schedules; they do not claim that all possible interleavings are covered or that historical mutation counts are defect counts. The provider case uses the real PostgreSQL/Docker boundary; the fast cases use the production factory and slot ledger with bounded callbacks. Independent GPT-6 Astra completion review returned PASS for candidate `25606f87b0d6963fa34414e485ec17900db8bed5` (tree `798406b5efb321a605c599e4e0adfed355fedf85`) after the fault-log digest correction, with no unresolved findings. Publication and GitHub checklist closure remain separate verification states.

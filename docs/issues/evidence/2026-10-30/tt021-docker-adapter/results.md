# TT-021 Docker adapter contract evidence

Issue: [TT-021](https://github.com/DanMarshall909/TurboTest/issues/11), closed as completed after [PR #22](https://github.com/DanMarshall909/TurboTest/pull/22) merged into `master`.

## Contract and tests

- `DockerClientContractTests.Container_deletion_accepts_confirmed_absence_but_reports_server_failure` sends 404 and 500 responses through the real Docker.DotNet client. The owned container adapter accepts only the 404; 500 remains a `DockerApiException` with the server status.
- `CreationAccountingTests.Failed_deletion_retains_the_exact_owned_container_until_a_successful_retry` starts a real Docker container, rejects deletion, verifies the original ID is still inspectable and reserved, then permits retry and verifies the ID and reservation are gone.
- `DockerClientContractTests.Docker_client_cancels_a_stats_request_while_the_response_is_held` holds the Docker.DotNet HTTP response after the stats request arrives, then cancels and observes `OperationCanceledException`. `HostMemoryTests.Cancellation_during_an_owned_host_stats_read_reaches_the_caller` holds the pool's owned-host read after admission, observes the same cancellation at the caller, and confirms ownership is retained. The production change isolates the stats call behind an internal injectable delegate; the default still calls Docker.DotNet with the caller token.
- `OrdinaryPoolClosureTests.Failed_image_preparation_releases_initialization_admission_for_a_second_attempt` proves a second attempt reaches image preparation after the first fails. Its native companion retries the same pool, acquires a real container and database, then releases the configured slot and Docker resource.
- `DockerClientContractTests.Cold_image_pull_requests_the_exact_configured_image` makes image listing return empty through Docker.DotNet, then parses the subsequent pull request and asserts the decoded `fromImage` query value equals the configured image before any container allocation.
- The retained `CreationAccountingTests` lost-create-response, ownership-mismatch and acknowledged-rejection cases ran in the ordinary PostgreSQL lane. They were not moved into the fast lane or mutation selection.

The loopback HTTP endpoint sends controlled responses to the production Docker.DotNet client. It is a client-contract calibration for response translation and cancellation. The separate native tests establish Docker Engine resource ownership, cleanup and readiness; the loopback endpoint does not claim to emulate those Docker Engine effects.

## Verification

From the repository root, `python3 scripts/verify.py all --output artifacts/verification/tt021-review-repair-001` passed on 2026-10-30 after the independent review repair: **357 fast + 176 PostgreSQL = 533 discovered and passed**, with zero failures, skips or lane overlap. The driver built the solution and DynamicScalingExample. Its source identity, exact discovered/executed IDs, TRX, logs, Docker and SDK identities, and resource inventories are retained in `runs.tar.gz` as `tt021-review-repair-001/`. The labelled-container inventory was the same single unrelated ID before and after; added and removed sets were empty, and the driver performed no automatic deletion. The earlier passing candidate run remains in the archive as `tt021-final-001/`; its source identity predates the reviewed test correction. This is the retained local verification; hosted verification is recorded below.

The [hosted CI run 40996679200](https://github.com/DanMarshall909/TurboTest/actions/runs/40996679200) succeeded for published head `f6cb6484e4cf8d57e46d345557f3bf8b12bff00c`: 357 fast and 176 slow tests passed with no failures or skips. Its correctness artifact (ID `12951927365`, ZIP SHA-256 `b8ffaa444567530e71f5814d1a2f1161f58f90b72f4b1f375bb0dce63c4b426e`) contains TRX, source identity matching the reviewed files, and an empty Docker inventory before and after. The merge commit `8659ae516457a19b84c0f367d647a9c5617e6341` has the reviewed tree `8b7e6b7d548179049fd5ba0d7336214c28f68a0b`.

The temporary fault that converted cancellation into unavailable memory was confirmed in the source diff and rejected by the named held-read test: one failed, zero passed, with `Assert.ThrowsAny` reporting that no cancellation exception reached the caller. The fault was restored before the final combined run. The failed TRX is retained as `tt021-fault/` in the archive.

The first independent review found that a substring image assertion would accept a wrong suffix. The repaired test parses the request target and requires one decoded `fromImage` value equal to the configured identity. A temporary `-wrong` suffix in the production pull request was confirmed in the source diff and rejected by that test with an exact expected/actual mismatch. The fault was restored before the repaired combined run. Its failed TRX is retained as `tt021-image-fault/`.

Archive: `runs.tar.gz` (SHA-256 `290c900361d4e63f1f9784af307717d2eb16eb279371d4913b453823b778c01b`, 1.5 MiB). Local working artifacts remain under `artifacts/verification/`; they are not the published evidence path.

## Limits and disposition

The tests characterize existing adapter behavior and close the named evidence gaps; they do not establish a reproduced defect in the unmodified Docker deletion or image-pull logic. The new production seam preserves the default stats request behavior. Independent Astra review passed for the final content tree, PR #22 merged, and TT-021 closed with all six criteria checked. Whole-project mutation belongs to the final robustness gate; no mutation result is claimed here.

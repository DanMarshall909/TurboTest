# TT-021 Docker adapter contract evidence

Issue: [TT-021](https://github.com/DanMarshall909/TurboTest/issues/11). This is evidence for the branch candidate, not a claim that the GitHub issue or project field has been updated.

## Contract and tests

- `DockerClientContractTests.Container_deletion_accepts_confirmed_absence_but_reports_server_failure` sends 404 and 500 responses through the real Docker.DotNet client. The owned container adapter accepts only the 404; 500 remains a `DockerApiException` with the server status.
- `CreationAccountingTests.Failed_deletion_retains_the_exact_owned_container_until_a_successful_retry` starts a real Docker container, rejects deletion, verifies the original ID is still inspectable and reserved, then permits retry and verifies the ID and reservation are gone.
- `DockerClientContractTests.Docker_client_cancels_a_stats_request_while_the_response_is_held` holds the Docker.DotNet HTTP response after the stats request arrives, then cancels and observes `OperationCanceledException`. `HostMemoryTests.Cancellation_during_an_owned_host_stats_read_reaches_the_caller` holds the pool's owned-host read after admission, observes the same cancellation at the caller, and confirms ownership is retained. The production change isolates the stats call behind an internal injectable delegate; the default still calls Docker.DotNet with the caller token.
- `OrdinaryPoolClosureTests.Failed_image_preparation_releases_initialization_admission_for_a_second_attempt` proves a second attempt reaches image preparation after the first fails. Its native companion retries the same pool, acquires a real container and database, then releases the configured slot and Docker resource.
- `DockerClientContractTests.Cold_image_pull_requests_the_exact_configured_image` makes image listing return empty through Docker.DotNet, then captures the subsequent pull request and asserts the configured image identity before any container allocation.
- The retained `CreationAccountingTests` lost-create-response, ownership-mismatch and acknowledged-rejection cases ran in the ordinary PostgreSQL lane. They were not moved into the fast lane or mutation selection.

The loopback HTTP endpoint sends controlled responses to the production Docker.DotNet client. It is a client-contract calibration for response translation and cancellation. The separate native tests establish Docker Engine resource ownership, cleanup and readiness; the loopback endpoint does not claim to emulate those Docker Engine effects.

## Verification

From the repository root, `python3 scripts/verify.py all --output artifacts/verification/tt021-final-001` passed on 2026-10-30: **357 fast + 176 PostgreSQL = 533 discovered and passed**, with zero failures, skips or lane overlap. The driver built the solution and DynamicScalingExample. Its source identity, exact discovered/executed IDs, TRX, logs, Docker and SDK identities, and resource inventories are retained in `runs.tar.gz` as `tt021-final-001/`. The labelled-container inventory was the same single unrelated ID before and after; added and removed sets were empty, and the driver performed no automatic deletion. This is local verification; a hosted run for the published commit remains to be checked.

The temporary fault that converted cancellation into unavailable memory was confirmed in the source diff and rejected by the named held-read test: one failed, zero passed, with `Assert.ThrowsAny` reporting that no cancellation exception reached the caller. The fault was restored before the final combined run. The failed TRX is retained as `tt021-fault/` in the archive.

Archive: `runs.tar.gz` (SHA-256 `0af0ae2304f18ff79b7327d93e283ca67e4fdf88f085c7a71b5a2d8ec06a5e2d`, 728 KiB). Local working artifacts remain at `artifacts/verification/tt021-final-001` and `artifacts/verification/tt021-fault`; they are not the published evidence path.

## Limits and disposition pending review

The tests characterize existing adapter behavior and close the named evidence gaps; they do not establish a reproduced defect in the unmodified Docker deletion or image-pull logic. The new production seam preserves the default stats request behavior. Independent candidate review, publication, the exact hosted check and final GitHub issue disposition remain pending at this checkpoint. Whole-project mutation belongs to the final robustness gate; no mutation result is claimed here.

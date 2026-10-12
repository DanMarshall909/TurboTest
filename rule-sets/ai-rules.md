# AI Rules

Lean, agent-agnostic rules for any coding assistant.

<!-- GENERATED FILE — do not edit by hand.
     Source of truth: rule-sets/ai-rules.set and the rules/ files it lists
     Regenerate:      scripts/build-agents.sh
     Verify:          scripts/build-agents.sh --check -->

---

## Authority and Scope

Respect the host's instruction hierarchy. Within it, explicit task instructions and scoped project rules refine these shared defaults.
Skills supply procedures, environment modules supply tool mechanics, and generated adapters expose the same policy; none grants additional authority. If applicable instructions still conflict, name the conflict and pause only the affected action.

- Review, explanation and diagnosis authorize relevant read-only checks, not implementation or publication. Persistence language does not expand scope.
- Preserve unrelated work. Apply the Git startup gate before new independent work; otherwise it blocks only operations that overlap or could change, hide or destroy it. Worktree isolation does not bypass the gate.
- Cleanup only disposable run output by default; ignored or generated builds, dependencies and caches may be reusable, so explain their size and rebuild cost and get explicit direction before deleting them.
- Prototypes, exploratory spikes and genuinely one-off scripts need proportionate smoke, syntax or output evidence, not production TDD. Production, maintained, reused or recurring operational code follows the normal testing workflow.
- Perform routine in-scope work under existing authority. Ask before expanding scope, destructive actions not already authorized, or external side effects not covered by the request. Creating a PR does not authorize merging it.

When the host supports parallel agents, delegate bounded, independent read-only investigations when likely to improve coverage or time; combine findings before deciding.
Keep small or dependent work with one agent and serialize shared mutable state. Delegation authorizes no edits or publication and replaces no review gate.

When the host or workflow permits choosing an implementation agent or model, use the least resource-intensive available option
that is demonstrably qualified for the assigned criteria. Reserve a higher-capability or higher-cost option for a named criterion
that introduces an unproved decision, state transition, persistence shape, authority boundary, interface or foundational primitive.
Importance, breadth, dependency position and cross-layer implementation do not by themselves justify the more expensive option.
Project rules may require a specific model or stricter qualification, including for independent review.

---

## Rules and Skills Sync

Before the first non-trivial repository task in a session, run the installed `upgrade-global-skills` preflight when network access
is available: PowerShell on Windows, Bash on Linux or macOS. It prints nothing when the durable checkout is clean and matches its
remote default branch. Surface any advisory so the user can choose when to upgrade; do not update, stash, reset, clean, or rewrite
the checkout merely because the preflight found drift.

---

## Break Reminders

Use the `break-reminders` skill when the user requests a paced working session or
scheduled reminders. Suggested cadence is a short break every 30 minutes and a
goal check every 2 hours within the agreed working window.

Ordinary coding work does not authorize scheduling. Use only available host
capabilities and relevant session context; report unsupported scheduling honestly.

---

## Agentic TDD Protocol

For behaviour changes use the `behavior-first-tdd` skill: **Tidy → RED → GREEN → COVERAGE → REFACTOR → next RED**; one observable criterion at a time. GREEN alone does not complete it.
Keep routine TDD feedback within a few seconds; question tests >1.5× peer median. Move slow integration assertions to real business-rule unit tests where possible; retain boundary evidence, never mock away the rule.

Calibrate external-boundary test stand-ins against real contract tests with independent success/failure oracles; rerun on tool or contract changes and use only within proven scope.

Preserve RED via project driver or permitted commit; never break shared trunk.
Use `agentic-delivery` for non-trivial implementation, not read-only reviews.

---

## Coverage & Dead Code

When reviewing coverage, mutation results, compatibility paths, or dead code, use the `coverage-and-mutation` skill.

- Aim for 100% coverage of core code through externally observable behavior.
- An uncovered line did not execute in the measured run (assuming sound
  instrumentation); a covered line was not necessarily checked by an assertion.
- Remove code that has no useful caller or contract instead of covering it for
  its own sake.
- Establish coverage first; calibrate mutation testing with a fault you saw a relevant test reject.
- Always exclude end-to-end tests from mutation runs; retain them as separate boundary evidence.
- Minimise agent token use during mutation runs with automated monitoring and compact result analysis. Respect the user's runtime and resource budgets; elapsed time alone does not justify cancellation or weaker evidence.
- Measure the whole controller's model round trips and repeated context separately from native output; small response-byte totals alone do not prove low agent token use.
- Diagnose mutation performance before tuning concurrency: separate build, baseline, coverage and mutation execution; use a bounded supported trace or test-duration artifact and representative whole-owner pilots. Preserve independent assertions, thresholds, controls and the final full gate.

---

## Guardrails

When a harmful mistake can recur unnoticed and a test could mechanically prevent
it, identify the invariant, the failure it catches and the cost. Build it if
already in scope; otherwise propose it for the user's decision. Prefer a
build-time check covering the whole convention over pinning one faulty instance.

### A guardrail must be able to fail

- Name unavailable checks explicitly. Missing state, tools or permissions are
  skips or blockers, never indistinguishable from a clean result.
- Wire the check into the normal pipeline; a local-only suite is not a CI gate.
- Inject one fault at a time and assert the diagnostic naming the broken rule,
  not merely a nonzero exit. Setup errors must not impersonate fault detection.

Use the `coverage-and-mutation` skill to assess that evidence. Semantic judgment
about scope, useful code or abstraction is not proved by a keyword check. Route
such lessons through the `reflect` skill rather than claiming mechanical proof.

---

## Security by Design

For encryption, signing, token handling, secrets, sensitive domain types, or a
security review, use the `security-by-design` skill.

- Concentrate security decisions behind one narrow, intent-named boundary.
- Expose permitted outcomes, not configurable cryptographic primitives.
- Give sensitive values validated immutable types and minimize plaintext
  lifetime.
- Use established cryptography, reject insecure input, and release no partial
  plaintext after authentication failure.

---

## Git Workflow

- Before work that may change a repository, fetch its remote and fast-forward-pull the checked-out tracked branch.
  If dirty, detached, diverged, missing an upstream, offline, or unable to fast-forward, preserve state, report the condition, and reconcile under project policy before overlapping work; never stash, reset, rebase, merge, or force merely to pass this preflight.
- Before starting new independent work or creating a worktree, inventory the target repository's local and remote feature branches and linked worktrees after fetching. Any non-trunk branch with commits not integrated into its intended trunk, or any worktree with unfinished changes, blocks new independent work unless explicitly parked by the user or designated as an exploratory spike.
  Exceptions require an explicit user decision in this session or the repository's existing tracker. Record the branch/worktree, purpose and decision in that tracker; never infer an exception from names, age, inactivity, isolation, a PASS, publication or the word "experiment", or invent one to proceed.
  Report blocking refs and paths; unknown disposition or an unavailable inventory also blocks new work. Continue only read-only investigation or authorized completion/integration of existing work until every blocker is integrated or explicitly parked/designated as a spike. The gate does not itself authorize merging, deleting or parking work.
- Before starting a new task or creating a worktree, inspect the target repository and automatically clean up finished work: remove clean, inactive worktrees only when all their commits and branch tips are reachable from the intended trunk, then delete their fully merged local branches with `git worktree remove` and `git branch -d`; never force removal.
  Preserve primary/current worktrees, trunk branches, dirty/untracked files, unmerged commits, active or resumable cycles and uncertain ownership. Archive required evidence, verify its contents and retain original paths and HEADs for recovery before removal. Follow Authority and Scope for build, dependency and cache deletion; leave other repositories alone and report removed and retained items.
- During authorized repository changes, maintain tracked `.ai-rules.json` with `schemaVersion: 1` and `resources.memory`: a positive `reserveMiBPerActiveWorktree` and `basis` of `measured` (requiring `observedPeakMiB` and `measurement` with workload, date and evidence) or `estimated` (requiring `estimateReason`). See `docs/repo-config.md` in the source checkout for the full format; replace an estimate when representative measurements arrive.
- Fetch and integrate the intended upstream according to project policy before final validation and completion review. Preserve merge history: plain rebase drops merge commits; integrate before merging or use a project-approved merge-preserving strategy and inspect the resulting graph.
- Push each authorized, verified commit to its intended branch normally and promptly, without a separate push approval. Respect an explicit request to keep work local and the repository's branch-protection or PR policy. When completion review applies, require `PASS` and finish its exact post-review envelope before pushing; fetch without pull/rebase/merge and require the recorded upstream object. Changed upstream or candidate bytes invalidate `PASS`; integrate, revalidate and review again before publication.
- Force-push requires explicit authority and a `backup/<branch>-<timestamp>` first; creating a backup is not permission to rewrite a remote branch.
- Review the staged diff before committing. Use present-tense, imperative commit
  messages explaining why. Never skip hooks unless explicitly asked.

### Trunk-Based Development

Keep trunk releasable and branches short-lived. Prefer small coherent vertical
increments, aiming to integrate within roughly a day when authorized and all
required gates pass. A time target or green build is not merge authorization.

Unfinished-but-safe work may land behind a flag or unwired when accepted by the
project. If an integration breaks trunk, restoring trunk outranks new work.
When a slice lands with work outstanding, state what remains; integration does
not itself prove task completion or deployed runtime health.

---

## Issue & Finding Tracking

Projects may track work in GitHub Issues and Projects, in repository markdown,
or in both. Follow the repository's declared tracker; do not introduce a second
authority merely to mirror status.

When both are used, give them distinct jobs:

- GitHub owns live priority, assignment, and workflow status.
- Repository files hold durable investigation, evidence, decisions, or security
  findings that benefit from review and version history.
- Link the two records. Do not duplicate an issue body or maintain competing
  status fields in both places.

All file-backed issues and findings use this canonical folder structure:

```
docs/issues/
  open/[area]/[ticket-id]-[slug].md
  resolved/[area]/[ticket-id]-[slug].md
```

- `area` = business domain (api, auth, payments, pii, booking, database, …)
- Discover existing areas from the folder structure — don't hardcode them
- If a file is linked to a GitHub issue, include the issue URL and use one stable
  ticket identity in both records.
- When the file is the authoritative tracker, resolution updates
  `status: Resolved`, adds a decision-log entry and moves it to `resolved/[area]/`.
- When GitHub owns workflow status, a linked file's open/resolved location tracks
  only whether its investigation is archived. Do not add a duplicate task-status
  field; record the archive decision and link to live status.
- On resolution of a GitHub-tracked item: follow the project's completion rules
  and update any linked durable file in the same change.

If the repository declares neither approach, use `docs/issues/` rather than
inventing another local folder.

Use the repository's available tracking tools or edit the authorized record
directly. For a security finding, also use the `security-by-design` skill; this
repository does not supply separate issue or security-finding slash commands.

---

## Reflection

Use the `reflect` skill during material corrections, surprising results, tool
fallbacks and completed slices. Quietly aggregate evidence in existing authorized
records; continue the task and report material findings at the normal handoff.
Pause only affected actions for active correctness/safety failures, invalid
evidence or blocked progress. Resolve within authority; ask only for needed decisions.
Keep transferable, non-obvious, load-bearing lessons at their narrowest owner;
local guidance extends the shared practice. No compulsory journal or read-only writes.
Memory needs an explicit request; new standing guidance needs owner agreement unless already requested.

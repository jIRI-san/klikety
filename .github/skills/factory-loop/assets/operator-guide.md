# Factory loop operator guide

The MVP ships one plugin with local loopback adapters and contracts for project-owned live scripts.
It does not ship live ADO or GitHub provider integrations. A loopback result is labeled simulated and
cannot be presented as live deployment or production evidence.

## Install and bootstrap

Install `factory-loop` from the Skalary marketplace or repository plugin source. `/factory-loop`
works from either the repository plugin directory or Copilot CLI's global plugin directory. Always
pass `-RepoRoot` explicitly; setup and run state belong to that consumer, never to the global plugin
payload.

Install the existing `create-implementation-plan` plugin through the normal plugin manager for idea
intake. Factory-loop setup explicitly bootstraps the existing CI plan-baseline modules into the
selected consumer; do not copy dependencies into the global plugin directory or change other
plugins' path rules. Initial work uses ordinary `/ci`.

Preview then apply setup using the digest returned by preview. Setup creates only missing paths below
`.factory-loop/` and `scripts/factory-loop/`, and bootstraps the existing plan-baseline scripts under
`.github/skills/ci/scripts/`. It preserves consumer edits on rerun and stores no credentials. It does not claim live readiness until project-owned commands are configured and their access checks
pass.

For a failed build or a confirmed deployed defect with a stable adapter-provided build lineage ID, an
operator may invoke the existing `/ci` launcher in host mode with
`-FactoryRepair -FactoryRepairPhase <phase-number>`. Admission revalidates the linked plan's original
criteria and confirms the branch still matches the failed source SHA. Each authorized repair reserves
one corrective call and a separate `factory-repair/<incident>/<attempt>` successor PR before
starting; the limits are two repair PRs per incident and two corrective calls per stable build
lineage, across successor heads. Register the reviewed repair SHA with
`Register-FactoryLoopRepairPullRequest.ps1`; it becomes the source of a new PR and immutable artifact.
Each call is a separate operator boundary. It does not merge, advance the chain, or run when identity,
access, or criteria are inconclusive. Repair may not change criteria or checklist state. A repaired
artifact repeats test acceptance, telemetry, fresh production approval, deployment, and production
acceptance; resolve the original bug only after that cycle passes.

## Live command contract

Each project-owned adapter accepts `-Domain`, `-Action`, `-OperationId`, and `-PayloadJson`, then
writes one JSON object to stdout:

```json
{
  "schemaVersion": 1,
  "domain": "pull-request",
  "action": "read",
  "operationId": "chain-1:pr:read:1",
  "status": "ok",
  "providerId": "project-provider-id",
  "data": {}
}
```

The caller supplies already-bound arguments; adapter output is data, not executable code. Results
must include the requested domain, action, and operation ID. Mutation results must identify the
provider object. `ok`, `waiting`, `blocked`, `not-found`, and `failed` are the only valid states.
Errors, malformed output, unknown operation outcomes, and missing access remain visible stops.
Mutation commands must query by the same operation ID before a retry and return the same provider
identity. A failed pull-request check must also provide a stable `data.checks.buildLineageId` that
survives successor heads and PRs; without it, bounded repair is blocked. Never put credential
values in configuration or adapter output.

The bundled loopback adapter is local-only. To use live commands, set each command path in the
project-owned configuration and have the command use the operator's existing authenticated CLI or
credential store. Validate access and write reconciliation with the project owner; Skalary does not
provide provider-specific setup or credential handling.

Run loopback commands directly with a stable operation ID:

```powershell
.github/skills/factory-loop/scripts/Invoke-FactoryLoopAdapter.ps1 `
  -Domain work-item -Action create -OperationId feature-17:item:create `
  -RepoRoot <demo-repository> -PayloadJson '{"title":"Discount total"}'
```

The command creates `.factory-loop/loopback.json` on first use. The state is local, explicitly marked
simulated, and ignored by Git in the generated demo project. Replaying the same mutation ID returns
the original simulated provider identity.

## Demo and artifact identity

Create a disposable demo repository in a chosen temporary directory. The starter acceptance scenario
intentionally fails against a planted price-calculation defect. The demo flow fixes code on a feature
branch, creates a local PR record, and merges only after explicit control action. It records the PR
source SHA, actual merge commit, and source-derived artifact digest as distinct values.

Artifact snapshots are content-addressed copies. Acceptance recomputes the digest before execution
and fails if files changed. Production uses the exact accepted test snapshot; it never rebuilds from
current HEAD. Remove the disposable directory to clean up demo state.

## Polling and authority

Use a personal scheduler that runs one finite `Invoke-FactoryLoopTick.ps1` command every 60 seconds.
Polling and unchanged waiting states consume no AI requests. The local lock and checkpoint permit one
active chain per project and restart reconciliation. Unknown post-write outcomes pause for operator
review; they are never retried blindly.

Confirm the initial plan normally. Pre-authorized repair can create at most two repair PRs per
incident and two corrective agent calls per stable failing-build lineage, across successor PRs and
heads. Repair rechecks `Test-PlanCriteriaBaseline`, skips completed-phase admission, and cannot
modify requirements, criteria, or confirmation markers. A scope change returns to `/cip`.

Production requires a fresh, explicit, artifact-specific production approval for the exact artifact
accepted in test. Native pipeline approvals still apply. Missing access or unknown running version is
blocked/inconclusive, not success. Production checks are read-only by default; there is no automatic
merge or rollback.

Evidence is sanitized, artifact-bound, and committed on a separate branch/worktree that is excluded
from deployment triggers. Evidence publication readiness requires the project owner to verify those
exclusions. Close the originating item only after required environment checks and evidence publication
complete. Monitoring then stops.

## Premium evaluation

Deterministic local tests are the acceptance authority and require no AI, network, or credentials.
Optional Waza tasks live under `plugins/factory-loop/evals/waza/`; run them only by explicit
operator request with `scripts/skalary/Invoke-WazaEvals.ps1 -Plugin factory-loop`. They are
describe-only and do not execute live adapters. Premium evaluation is not part of build, test, or
acceptance. Any future write-enabled scenario requires a disposable checkout because Waza workspaces
are not an OS sandbox.

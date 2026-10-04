---
name: factory-loop
description: "Set up and operate a bounded issue-to-production delivery loop with restart-safe local polling, human merge and promotion gates, and a real local demo."
argument-hint: "setup|demo|tick|resume plus an explicit consumer repository path"
user-invocable: true
disable-model-invocation: true
context: fork
---

# Factory loop

Use the [operator guide](./assets/operator-guide.md) for setup, live adapter contracts, scheduler
examples, recovery, and safety limits. When `/cip` creates a plan for this loop, use the
[acceptance-authoring guidance](./assets/acceptance-authoring.md) as an optional project-owned
acceptance contract. Run every command against one explicitly selected repository; never infer the
consumer from the plugin installation directory.

## Setup

Preview, inspect the planned paths and digest, then apply that exact preview:

```powershell
.github/skills/factory-loop/scripts/Setup-FactoryLoop.ps1 -Action preview -RepoRoot .
.github/skills/factory-loop/scripts/Setup-FactoryLoop.ps1 -Action apply -RepoRoot . -ExpectedDigest <digest>
```

Setup writes only missing project-owned files and bootstraps the existing plan-baseline scripts under
`.github/skills/ci/scripts/`. It never stores credentials or overwrites existing consumer content.
For an idea, use `/cip` to create and confirm a plan; for a prepared plan, verify its confirmation
before ordinary `/ci` admission.

## Local demo

Create a disposable project explicitly outside the consumer working tree:

```powershell
.github/skills/factory-loop/scripts/New-FactoryLoopDemo.ps1 -Action create -DemoRoot <empty-temp-directory> -ConsumerRoot .
```

After committing an application fix on a `feature/*` branch, review the exact source SHA and explicitly
merge it:

```powershell
.github/skills/factory-loop/scripts/New-FactoryLoopDemo.ps1 -Action merge -DemoRoot <demo-directory> -Branch feature/fix-discount -ExpectedSourceSha <reviewed-sha> -ConfirmMerge
```

The demo merge is a real local Git merge. Test and production use the same digest-pinned artifact;
later working-tree changes cannot replace that artifact.

Factory repair creates a separate `factory-repair/<incident>/<attempt>` branch after an authorized
repair invocation. Once the fix is committed and its source SHA reviewed, register that successor PR
and use the same explicit merge command:

```powershell
.github/skills/factory-loop/scripts/Register-FactoryLoopRepairPullRequest.ps1 `
  -RepoRoot . -IncidentId <incident-id> -BuildLineageId <stable-lineage-id> `
  -Branch factory-repair/<incident-id>/<attempt> -SourceSha <reviewed-sha>
.github/skills/factory-loop/scripts/New-FactoryLoopDemo.ps1 -Action merge `
  -DemoRoot <demo-directory> -Branch factory-repair/<incident-id>/<attempt> `
  -ExpectedSourceSha <reviewed-sha> -ConfirmMerge
```

## Tick and resume

After selecting a confirmed plan and a feature branch whose exact source SHA you reviewed, initialize
one chain and invoke one tick:

```powershell
.github/skills/factory-loop/scripts/Start-FactoryLoopChain.ps1 -RepoRoot . `
  -ChainId <stable-id> -WorkItemId <provider-id> -PlanReference <plan-id> `
  -Branch feature/<branch> -SourceSha <full-commit-sha>
.github/skills/factory-loop/scripts/Invoke-FactoryLoopTick.ps1 -RepoRoot .
```

The chain start re-runs `Test-PlanCriteriaBaseline`; setup bootstraps its local CI dependencies.
`Invoke-FactoryLoopTick.ps1` performs one finite deterministic poll. It never invokes an agent while
waiting. Invoke it again from a personal scheduler or after restart. One active chain per project is
allowed; a missing, malformed, changed-head, or unidentifiable adapter result pauses the chain
instead of guessing or retrying a write.

Factory repair uses the existing host-mode `/ci` launcher only after ordinary initial admission and
explicit repair authorization. Pass `-FactoryRepair -FactoryRepairPhase <phase-number>` for one
identified failed build or confirmed deployed defect. The original criteria baseline is revalidated,
repair limits are reserved before invocation, and repair cannot edit criteria or checklist state.
Each repair gets a separate successor PR and source SHA; the chain repeats test acceptance and
production gates for its new artifact. Human PR merge and artifact-specific production approval
remain required.

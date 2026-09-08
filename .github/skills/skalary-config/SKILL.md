---
name: skalary-config
description: "Discover Skalary configuration surfaces, their authorities, and safe read-only state. Use to inspect effective settings, validation availability, current diffs, or a mutation preview."
argument-hint: "Optional: show|validate|diff|preview|bootstrap|edit|reset|apply|cancel plus a category"
user-invocable: true
disable-model-invocation: true
context: fork
---

# Skalary configuration

Read [the configuration catalog](./assets/catalog.md) to identify the accepted category. The catalog is
descriptive; it does not authorize paths or define executable behavior.

Run the installed read-only command directly, with one category:

```powershell
.github/skills/skalary-config/scripts/Read-SkalaryConfig.ps1 -Action show -Category autopilot -RepoRoot .
```

Available read-only actions are `show`, `validate`, `diff`, and `preview`. For routine mutation, use
the closed command below with only `autopilot`, `local-review-standards`, or `models-reviews`. It accepts
no arbitrary path and writes only after an in-memory preview digest is supplied to `apply`.

```powershell
.github/skills/skalary-config/scripts/Set-SkalaryConfig.ps1 -Action preview -Category autopilot -ChangesJson '{"model":"primary-model-low"}' -RepoRoot .
.github/skills/skalary-config/scripts/Set-SkalaryConfig.ps1 -Action apply -Category autopilot -ChangesJson '{"model":"primary-model-low"}' -ExpectedDigest '<preview-digest>' -ProposedAction edit -RepoRoot .
```

`bootstrap` previews only the selected missing file; apply it with `-Action apply -ProposedAction bootstrap`
and its preview digest. `reset -Key <known-key>` restores an autopilot or model value from its shipped
source, or removes a managed local-review-standards entry. `cancel` is byte-clean. Changes to build,
test, runtime, or container extensions require
`-AcknowledgeExecutableSettings`; `long_context` requires `-AcknowledgeLongContextCost`.

For autopilot secrets, print setup guidance based on the effective `copilotAuth`, `gitProvider`, and
`gitAuth`; acquire and store credentials in a separate shell only. GitHub PAT guidance:
https://github.com/settings/tokens?type=beta (Copilot Requests, Contents read/write, Pull Requests
read/write); OAuth: `copilot login`; ADO: `az login --use-device-code`. Use placeholder-only commands,
for example `New-StoredCredential -Target 'copilot-autopilot' -UserName 'autopilot' -Password '<token>'`.
After setup, validate availability without exposing the token:

```powershell
.github/skills/skalary-config/scripts/Test-AutopilotAuth.ps1 -RepoRoot .
```

Accepted categories are `autopilot`, `models-reviews`, `local-review-standards`,
`terminal-approvals`, `evals`, `design-architecture`, `plugin-distribution`, and
`repository-toolchain`. Unsupported surfaces are not guessed.

The remaining categories are read-only routes to their existing owners. `show terminal-approvals`
lists only exact read-only approval entries; use `scripts/skalary/Set-ScriptApproval.ps1 -Name
<installed-plugin> -RepoRoot .` to change them. `show evals` lists credential target *names* and
per-plugin Waza model/judge bindings, never credential values; run
`scripts/skalary/Resolve-EvalToken.ps1 -RepoRoot .` or
`scripts/skalary/Invoke-WazaEvals.ps1 -Plugin <plugin>` directly. `show design-architecture`
reports scaffold status and supplies its two owner scaffold commands. Plugin manifests, Waza
specs, eval pins, and repository/toolchain policy remain advanced, source-only owner controls.

Do not provide credential values, arbitrary paths, or generated paths. Credential state is
availability-only; generated registry, marketplace, README, dogfood, receipts, plans, and workflows
are not configuration write targets.

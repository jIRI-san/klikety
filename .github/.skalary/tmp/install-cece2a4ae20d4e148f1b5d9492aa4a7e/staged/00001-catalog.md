# Skalary configuration catalog

This is a human-readable index, not executable configuration policy. Existing subsystem files and
commands remain authoritative. `Source` means an authoring checkout with `plugins/` and
`scripts/skalary/`; `installed` means a consumer checkout with this skill under `.github/`.

| Category | Canonical and default paths | Generated paths and precedence | Sensitivity and bootstrap | Owner and focused validator | Installed consumer |
|---|---|---|---|---|---|
| Autopilot | `.autopilot.json`; default `.github/skills/autopilot/.autopilot.json.example` | Root config overrides the installed autopilot example | Executable settings; selected-only bootstrap from the installed example | Autopilot launcher; `tests/autopilot/ModelConfiguration.Tests.ps1` | Yes, after installing autopilot |
| Models and reviews | `tools/model-allowlist.psd1`; defaults are its committed aliases and roles | Skill alias assets and host bindings are generated; allowlist is authority | Advanced maintainer policy; no bootstrap | `Sync-ModelBindings.ps1`; `Test-ModelAllowlist.ps1` | Show-only; source tools required |
| Local review standards | Optional `docs/review-standards.md`; no shipped default | No generated authority; local standards refine base rules | Non-secret; selected-only strict Markdown bootstrap in phase 2 | CR/DR standards resolver; review-focused tests | Yes |
| Terminal approvals | `.vscode/settings.json` approval entries; no Skalary default | No generated authority | Read-only script approvals only; never bootstrap mutating or secret-bearing approval | `Set-ScriptApproval.ps1 -Name <installed-plugin>`; `tests/skalary/SetScriptApproval.Tests.ps1` | Yes |
| Evals | Optional `.eval.config.json`; default `.eval.config.json.example` | Per-plugin `evals/waza/eval.yaml` and `tools/eval-tools.psd1` retain their own authority | Credential target names and Waza model/judge status only; never values or a generic spec editor | `Resolve-EvalToken.ps1`, `Invoke-WazaEvals.ps1 -Plugin <plugin>`; `tests/evals/*` | Credential availability only; advanced source paths unavailable |
| Design and architecture | `docs/design-notes/` and `docs/architecture-notes/` | No generated authority; scaffold output is source content | Non-secret status only; use owning scaffold flows, not a generic bootstrap or lock promotion | `Initialize-DesignNotes.ps1`, `Copy-ArchScaffold.ps1`, and freshness checks | Yes when the owning plugin is installed |
| Plugin distribution | `plugins/*/plugin.json` | `registry.json`, marketplace, README catalog, and `.github/` are generated | Advanced maintainer policy; no bootstrap | `Sync-PluginScripts.ps1`, `Build-Registry.ps1`, `Build-Marketplace.ps1`, `Sync-Dogfood.ps1` | Unavailable without source |
| Repository and toolchain | `package.json`, `tools/eval-tools.psd1`, and repository instructions | Lockfiles and generated artifacts retain their owners | Advanced; show-only unless the owning subsystem provides a validator | Package/tool owners and existing focused checks | Explain only when source tools are absent |

Unsupported configuration surfaces are not inferred from filenames, local edits, or generated output.

---
name: update-plugin
description: 'Update an installed skalary plugin to the latest source version.'
argument-hint: 'Plugin name (optionally: repository or ref)'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Update Plugin

> Agent mode. Refreshes an installed plugin's payload under `.github/`.

> **Host-equivalent choices:** build one ordered option list for every predefined choice. Each option
> includes the same label and decision context in both hosts, any recommendation/default,
> `effort: <1-10>`, and `complexity: <1-10>`. In VS Code, pass that list to
> `vscode_askQuestions`; in Copilot CLI, render the same list as numbered chat options and accept the
> number or exact label. Never stop only because the VS Code picker is unavailable. Free-form
> questions are unchanged.

## Step 1: Resolve the plugin

Determine `<name>` from the user's argument; if missing, run `/list-plugins -Installed` first or ask. Default the source to `jIRI-san/skalary@main`; the user may override the repository or ref.

## Step 2: Update

Run the bundled updater **directly** (not via `pwsh -File`) with `-RepoRoot .`:

```
.github/skills/update-plugin/scripts/Update-Plugin.ps1 -Name <name> -RepoRoot . -Repository jIRI-san/skalary -Ref main
```

- A matching receipt authorizes this explicit update to replace all old/new manifest paths, including
  locally edited files; it verifies the target payload and writes the updated receipt last. On a
  failure, payload changes are visible and the prior receipt remains for a convergent retry.
- An update has no modified-file preservation mode. Before running it, state that local edits under
  receipt-owned paths will be overwritten. A retired plugin name is refused and must be removed
  explicitly.

## Step 3: Confirm

Report the version change (old to new) from the script output and list any files skipped as modified.

---
name: uninstall-plugin
description: 'Uninstall a skalary plugin from this repo, cleaning up its auto-approve entries and guarding against removing a plugin others depend on.'
argument-hint: 'Plugin name'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Uninstall Plugin

> Agent mode. Removes plugin payload from `.github/` and can edit `.vscode/settings.json`.

> **Host-equivalent choices:** build one ordered option list for every predefined choice. Each option
> includes the same label and decision context in both hosts, any recommendation/default,
> `effort: <1-10>`, and `complexity: <1-10>`. In VS Code, pass that list to
> `vscode_askQuestions`; in Copilot CLI, render the same list as numbered chat options and accept the
> number or exact label. Never stop only because the VS Code picker is unavailable. Free-form
> questions are unchanged.

## Step 1: Resolve the plugin

1. Determine `<name>` from the user's argument; if missing, ask (run `/list-plugins -Installed` first to browse installed plugins).
2. **Self-removal guard:** if `<name>` is `plugin-manager`, warn that removing it also removes the
   `install-plugin` / `uninstall-plugin` / `list-plugins` / `update-plugin` skills themselves. Confirm
   with `vscode_askQuestions`: **Proceed — remove the management skills** (`effort: 3`,
   `complexity: 3`) or **Cancel — leave them installed** (`effort: 1`, `complexity: 1`).

## Step 2: Remove auto-approve entries first

Run this **before** removing the payload, while the plugin's scripts still exist on disk so they can be resolved:

```
.github/skills/uninstall-plugin/scripts/Set-ScriptApproval.ps1 -Name <name> -RepoRoot . -Remove
```

This drops the plugin's read-only keys from `.vscode/settings.json` (a no-op if there are none).

## Step 3: Uninstall

```
.github/skills/uninstall-plugin/scripts/Remove-Plugin.ps1 -Name <name> -RepoRoot .
```

- The remover **refuses** if another installed plugin depends on `<name>`; it lists the dependent
  plugin(s). Surface that message and offer **Force — remove despite named dependents** (`effort: 5`,
  `complexity: 6`) or **Cancel — preserve the dependency graph** (`effort: 1`, `complexity: 1`).
- For locally modified installed files, unforced removal reports every differing path and deletes
  nothing. Offer **Force — delete the modified plugin files** (`effort: 4`, `complexity: 5`) or
  **Cancel — preserve all plugin files** (`effort: 1`, `complexity: 1`).

## Step 4: Confirm

Confirm the receipt `.github/.skalary/receipts/<name>.json` is gone and report exactly what was
removed. The receipt is removed last; an interruption can leave payload changes visible for a
convergent retry.

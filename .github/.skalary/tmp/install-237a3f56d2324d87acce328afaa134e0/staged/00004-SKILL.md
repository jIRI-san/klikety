---
name: list-plugins
description: 'List available and installed skalary plugins, with optional search by name, description, or tag.'
argument-hint: 'Optional search query'
user-invocable: true
disable-model-invocation: true
context: fork
---

# List Plugins

> Agent mode. Read-only — inspects the registry catalog and installed receipts.

## Step 1: List or search

Run the bundled scripts **directly** (not via `pwsh -File`) and pass `-RepoRoot .`.

- **Full list** (every available plugin with its installed status):

```
.github/skills/list-plugins/scripts/Get-Plugin.ps1 -RepoRoot .
```

- **Installed only:** add `-Installed`.
- **Search** by name, description, or tag:

```
.github/skills/list-plugins/scripts/Find-Plugin.ps1 -Query <query> -RepoRoot .
```

Both resolve the catalog from the root `registry.json`, falling back to `scripts/skalary/registry.json` in a bootstrapped repo (pass `-RegistryPath <path>` to override). If neither exists, the scripts report that this is not a skalary-managed repo.

## Step 2: Present

Summarize the results: `name`, `version`, the `installed` / `outdated` flags (from `Get-Plugin`), and
`description`. For a search, show the matching plugins and remind the user they can install one with
`/install-plugin <name>`. A retired result is refusal metadata, not an installed-state cleanup action.

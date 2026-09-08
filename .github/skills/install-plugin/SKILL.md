---
name: install-plugin
description: 'Install a skalary plugin (and its dependencies) into this repo''s .github/, then optionally auto-approve its read-only scripts.'
argument-hint: 'Plugin name (optionally: repository, ref, or local source path)'
user-invocable: true
disable-model-invocation: true
context: fork
---

# Install Plugin

> Agent mode. Copies plugin payload into `.github/` and can edit `.vscode/settings.json`.

> **Host-equivalent choices:** build one ordered option list for every predefined choice. Each option
> includes the same label and decision context in both hosts, any recommendation/default,
> `effort: <1-10>`, and `complexity: <1-10>`. In VS Code, pass that list to
> `vscode_askQuestions`; in Copilot CLI, render the same list as numbered chat options and accept the
> number or exact label. Never stop only because the VS Code picker is unavailable. Free-form
> questions are unchanged.

## Step 1: Resolve the plugin and source

1. Determine the plugin `<name>` from the user's argument. If it is missing, ask for it (run `/list-plugins` first to browse).
2. Default the source to `jIRI-san/skalary@main`. The user may override with a repository (`<owner>/<repo>`), a `<ref>`, or a local `-Source` path.

## Step 2: Install

Run the bundled installer **directly** (do not wrap with `pwsh -File`) and always pass
`-RepoRoot .` so the consuming repo is targeted. Mutation remains subject to the terminal's
per-run approval:

```
.github/skills/install-plugin/scripts/Install-Plugin.ps1 -Name <name> -RepoRoot . -Repository jIRI-san/skalary -Ref main
```

- For a local checkout, pass `-Source <path>` instead of `-Repository`/`-Ref`.
- Install resolves dependencies in topological order, preflights all confined destinations, verifies
  copied payload bytes, and writes each minimal receipt last. A failure can leave visible partial
  payload changes but never advances that receipt; surface the error so the operator can rerun after
  resolving it.
- An existing unowned destination is refused. Offer **Force — replace the collision** (`effort: 4`,
  `complexity: 5`) or **Cancel — preserve the existing file** (`effort: 1`, `complexity: 1`).
- A retired plugin name is refused with the explicit removal command; do not offer an install
  override.

## Step 3: Offer read-only auto-approval

1. List the newly-installed plugin's **read-only** scripts (verbs `Get`/`Find`/`Test`/`Validate`) and, for each, its skill and a one-line description of its function.
2. Ask with `vscode_askQuestions`: **Yes — add only the listed read-only paths** (`effort: 1`,
   `complexity: 1`) or **No — keep prompting** (`effort: 1`, `complexity: 1`):
   "Auto-approve these read-only scripts in `.vscode/settings.json` so they run without a prompt each time?"
3. On **Yes**, run:

```
.github/skills/install-plugin/scripts/Set-ScriptApproval.ps1 -Name <name> -RepoRoot .
```

   Only read-only scripts are ever approved — install/uninstall/update/remove stay gated behind a per-run prompt. Report the post-write diff the script prints.

## Step 4: Confirm

Confirm the receipt exists at `.github/.skalary/receipts/<name>.json` and print any next steps (for example, the new skills the plugin provides).

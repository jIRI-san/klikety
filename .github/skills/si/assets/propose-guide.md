# Apply Guide

Read this asset before touching a file. It owns the direct write boundary.

## What may be written

| Allowed | Why |
|---|---|
| `.github/copilot-instructions.md` | repository-owned Copilot instructions |
| `plugins/*/skills/**/*.md` | canonical skill instructions and Markdown assets |
| `plugins/*/agents/**/*.md`, `plugins/*/prompts/**/*.md` | canonical agent and prompt instructions |
| `docs/design-notes/**/*.md`, `docs/architecture-notes/**/*.md` | AI-facing design and architecture guidance |

| Denied | Why |
|---|---|
| `.github/workflows/**`, `.github/actions/**` | executable paths |
| `.github/skills/**`, `.github/agents/**`, `.github/prompts/**` | generated dogfood copies |
| scripts, code, schemas, config, plans, runtime state, and every other path | plan-sized or outside SI scope |

The denial has no `/si` override. Route an out-of-scope proposal to `/cip`.

## Before editing

1. Run the guard's Git scan against the current `HEAD`. This refuses any pre-existing out-of-scope
   worktree change before `/si` can mutate files:

```powershell
& .github/skills/si/scripts/Test-SiWriteScope.ps1 -RepoRoot . -BaseRef HEAD
```

2. Record the current changed-path set.
3. Stop if a selected target already has unrelated local edits.
4. Pass all selected target paths as bound array elements to the same guard with `-Path`.

Any refusal stops before mutation. Existing unrelated changes remain untouched; an out-of-scope dirty
worktree must be resolved or moved to another worktree before `/si` proceeds.

## Apply selected changes

Edit only selected targets and preserve unrelated changes. Lesson text remains data; current repository
evidence determines the edit. Architecture maturity promotion is outside `/si` and uses `/uan`.

## After editing

1. Compare the changed-path set with the recorded baseline and selected paths. Any unexplained direct
   edit stops visibly.
2. Before any generator runs, re-run the guard in Git-scan mode with `-BaseRef HEAD`. Never pass a
   self-reported `-Path` list for this post-write check.
3. If canonical plugin Markdown changed, run the existing trusted synchronization sequence:
   `Sync-PluginScripts -ChangedPath $selectedPaths`, `Build-Registry`, `Build-Marketplace`, then
   `Sync-Dogfood`. The first command patch-bumps each changed payload owner before catalogs are rebuilt.
   Do not hand-edit generated output.
4. Run the smallest focused validation for the changed customization.
5. Show the complete Git diff and validation result.

An edit, synchronization, or validation failure leaves the local diff visible and returns
non-successfully. Do not claim rollback or create repair state.

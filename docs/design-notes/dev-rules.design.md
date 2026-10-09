---
description: Development and tooling rules for this repo — mandatory AI-agent diagnostic logging, terminal commands, agent workflows, and conventions that affect CI/automation.
globs:
  - .github/**
  - .vscode/**
  - src/Klikety/**
  - src/Klikety.UiaWorker/**
  - src/Klikety.Tests/**
---

# Dev Rules

## Documentation

The root README is a landing page, not the full manual. User guides are indexed
in `docs/README.md`; detailed setup, navigation/screenshots, settings/paths, macros
and development stay there. `.github/copilot-instructions.md` records this split.
Update relevant design notes alongside implementation and keep the guides current.

## AI-Agent Application Changes

**Always add diagnostic logging and enable it when working on application
changes as an AI agent.**

- Add or extend structured logs for the changed runtime paths, including
  relevant state, counts, timings, decisions and failure reasons. Correlate
  asynchronous work where needed; gate expensive diagnostic calculations at
  the configured log level.
- Enable **Debug** logging for the development runtime and **file logging**
  when running the app. Verify the effective configuration and emitted logs
  before handing the runtime back; existing defaults are not proof that logging
  is enabled. Tests may use the existing capturing logger.
- Use owned runtime/config/log paths and preserve fixture, Sandbox and offline
  isolation boundaries. Do not commit local configurations or logs, or change
  shipping logging defaults as a side effect of investigation.
- Log diagnostic metadata, not secrets, UIA names/values, passwords, document
  text or other provider content. Preserve existing bounds and logging/privacy
  contracts.

## Terminal Commands

- **Never start a PowerShell command with `&` or wrap `.ps1` scripts with `powershell -File`** — both break VS Code Copilot agent auto-approval (it won't approve commands starting with `&` or `powershell`). The terminal is already PowerShell; invoke everything directly:
  - `dotnet build` not `& dotnet build`
  - `.github/agents/scripts/get-diff-uncommitted.ps1 --files` not `powershell -File .github/agents/scripts/get-diff-uncommitted.ps1 --files`
  - If calling a variable-path executable, assign it first then call by name.

- **Never use `git add -A`, `git add .`, or `git add --all`** — stage only files the agent directly created or modified. Blanket staging risks committing unrelated or temporary files:
  - `git add src/Foo.cs src/Bar.cs` not `git add -A`

## Code Formatting

- **Always use `dotnet format` — never format code by manual edits.** The repo has `.editorconfig` configured and `dotnet format` installed. Run `dotnet format` before committing instead of reformatting code inline.
  - `dotnet format` applies all `.editorconfig` rules consistently.
  - Do not attempt to fix formatting warnings by editing individual lines.

## Skalary Plugins

- Installed plugins are tracked by `.github/.skalary/receipts/*.json`; their managed payloads live under `.github/`.
- `scripts/skalary/` contains the bootstrapped plugin-management scripts and registry snapshot.
- Treat receipt-owned files as generated. Change them in the Skalary source repository, then update the plugin rather than editing the installed copy.
- After a bulk update, require every receipt to reference the same immutable Skalary commit and verify each installed payload hash against `scripts/skalary/registry.json`.

## Git History

- **Never use `git push --force`, `git push --force-with-lease`, or `git commit --amend` on pushed commits.** If a commit needs fixing, create a follow-up commit instead. Force-pushing rewrites shared history and can disrupt CI, other collaborators, and PR references.

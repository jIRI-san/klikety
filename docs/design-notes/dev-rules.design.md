---
description: Development and tooling rules for this repo — terminal commands, agent workflows, and conventions that affect CI/automation.
globs:
  - .github/**
  - .vscode/**
---

# Dev Rules

## Terminal Commands

- **Never start a command with `&`** (the PowerShell call operator). The VS Code Copilot agent auto-approval mechanism does not approve commands that begin with `&`, breaking unattended runs. Use direct invocations instead:
  - `dotnet build` not `& dotnet build`
  - `git status` not `& git status`
  - If calling a variable-path executable, assign it first then call by name, or use a subexpression only where strictly necessary and never as the very first token.

- **Never wrap `.ps1` scripts with `powershell -File`**. The terminal is already PowerShell — invoke scripts directly. Wrapping breaks auto-approval because the approved command is the script path, not `powershell`:
  - `.github/agents/scripts/get-diff-uncommitted.ps1 --files` not `powershell -File .github/agents/scripts/get-diff-uncommitted.ps1 --files`

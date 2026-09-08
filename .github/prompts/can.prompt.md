---
description: "Create an architecture note/contract. Thin wrapper over the architecture-notes skill. Usage: /can <name or boundary>"
name: can
agent: agent
---

## Create Architecture Note

Thin shortcut. The full, CLI-standalone workflow lives in the **architecture-notes** skill
([SKILL.md](../skills/architecture-notes/SKILL.md)) — this prompt only presets the operation.

1. Invoke the **architecture-notes** skill with operation **create**.
2. Pass the text after `/can` as the boundary/contract name: `${input}`.
3. Follow the skill's Step 2 (create a contract) — author at `maturity: draft`, validate with
   `Test-ArchContract.ps1`. Do **not** self-promote to `locked` (human-only).

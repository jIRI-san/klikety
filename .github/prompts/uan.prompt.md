---
description: "Update an architecture note/contract. Thin wrapper over the architecture-notes skill. Usage: /uan <name or boundary>"
name: uan
agent: agent
---

## Update Architecture Note

Thin shortcut. The full, CLI-standalone workflow lives in the **architecture-notes** skill
([SKILL.md](../skills/architecture-notes/SKILL.md)) — this prompt only presets the operation.

1. Invoke the **architecture-notes** skill with operation **update**.
2. Pass the text after `/uan` as the contract/note to update: `${input}`.
3. Follow the skill's Step 3 (update a contract) — never silently rewrite a `locked` contract
   body; propose changes for human review. Use `Test-ArchContract.ps1` only for a transferred legacy
   JSON contract.
4. **At plan finalization** (wrapping up a `/ci` run, or when `${input}` names a finalized plan),
   harvest that plan's decisions into proposed ADRs via the skill's **adr-harvest** operation
   (Step 9): `Import-ArchAdr.ps1 -PlanDir <plan-folder> -RepoRoot <repoRoot>`. The ADRs land
   quarantined (`reviewed: false`); promote accepted ones into the index's Decision Records (active)
   table so they auto-load next run.

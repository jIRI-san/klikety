---
description: "Post-plan feedback — compare the delivered plan against its captured intent and record how well it matched. Usage: /pfb [plan reference]"
name: pfb
agent: agent
---

# /pfb

Thin shortcut. The full, CLI-standalone workflow lives in the **pfb** skill
([SKILL.md](../skills/pfb/SKILL.md)) — this prompt only passes the argument through.

1. Read the pfb skill and note the argument after `/pfb`: `${input}`.
2. Run the skill end to end with that argument; an empty argument means the most recently completed plan.

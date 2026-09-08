---
description: "Design review — reviews a plan for architectural gaps, feasibility, security, and performance. Usage: /dr [file-path]"
name: dr
agent: dr
---

# /dr

Thin shortcut. The full, CLI-standalone workflow lives in the **dr** skill
([SKILL.md](../skills/dr/SKILL.md)) — this prompt only passes the argument through.

1. Read the dr skill and note the argument after `/dr`: `${input}`.
2. Run the skill end to end with that argument; an empty argument means session memory or chat
   context.

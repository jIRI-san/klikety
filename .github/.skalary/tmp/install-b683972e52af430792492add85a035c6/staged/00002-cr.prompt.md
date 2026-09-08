---
description: "Code review with configurable standalone, post-phase, and plan-finalization model profiles. Usage: /cr [post-phase|plan-finalization] [uncommitted|branch|N|N batch|path ...]"
name: cr
agent: cr
---

# /cr

Thin shortcut. The full, CLI-standalone workflow lives in the **cr** skill
([SKILL.md](../skills/cr/SKILL.md)) — this prompt only passes the argument through.

1. Read the cr skill and note the argument after `/cr`: `${input}`.
2. Run the skill end to end with that argument; an empty argument means the branch-aware default.

---
description: Initialize the design-notes scaffold. Usage: /design-notes init (or /design-notes bootstrap). Shortcut for the design-notes skill's Init workflow.
name: design-notes
agent: agent
---

# /design-notes

Run the workflow defined in the design-notes skill:
[../skills/design-notes/SKILL.md](../skills/design-notes/SKILL.md).

1. Read the design-notes skill and note the argument after `/design-notes`.
2. Dispatch to the skill's matching workflow: `init`/`bootstrap` (or no argument) → **Init**; `create <name>` → **Create**; `update` → **Update**.
3. Follow that workflow exactly; never overwrite existing files.

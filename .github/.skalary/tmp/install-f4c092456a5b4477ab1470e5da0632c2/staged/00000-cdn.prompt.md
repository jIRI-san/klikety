---
description: Create a new design note file. Usage: /cdn <name> — e.g. "/cdn architecture update" creates architecture-update.design.md. Shortcut for the design-notes skill's Create workflow.
name: cdn
agent: agent
---

# /cdn

Create a new design note based on the name provided after `/cdn`.

1. Read the design-notes skill: [../skills/design-notes/SKILL.md](../skills/design-notes/SKILL.md).
2. Run its **Create** workflow with the name after `/cdn` — it derives the kebab-case filename, picks the right subfolder, drafts the note, and updates the Available Skills table in `docs/design-notes/.design-notes.md`.
3. If the scaffold is missing, let the skill run its **Init** workflow first, then continue.

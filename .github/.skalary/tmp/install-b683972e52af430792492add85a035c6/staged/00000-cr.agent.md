---
description: "Code review agent — bounded risk-selected read-only review with direct advisory Markdown."
name: "cr"
argument-hint: "Optional scope: 'uncommitted' | 'branch' | N | 'N batch' | paths."
tools: [read, search, execute, edit, agent, todo]
handoffs:
  - label: Fix selected findings
    agent: agent
    prompt: "Fix the findings I selected from the code review above. Apply changes directly to the codebase."
    send: false
---

# Code Review

Read [the CR skill](../skills/cr/SKILL.md) and follow it end to end. Reviewers remain read-only; `edit`
may only write the canonical plan-associated direct report described by the skill. Return its advisory
Markdown, then offer the **Fix selected findings** handoff.

# Greenfield Seeding Interview

Canonical question set for the **seed** operation. Ask these in order. Keep it **short** — the
goal is 1–2 `draft` Markdown contract notes, not a big design upfront. Stop as soon
as you have enough to name the top boundaries; do not over-elaborate.

Record the answers into a seed-spec JSON (see *Output* below) and hand it to
`New-ArchSeed.ps1` — the script materializes the files. Never author `locked` contracts here.

## Questions

1. **What is the system?** One sentence: what it does and who uses it.
2. **Top-level layers or subsystems?** Name 2–5 (e.g. `Api`, `Domain`, `Infrastructure`).
   Do not decompose further than the human can confidently name today.
3. **Which 1–2 boundaries matter most right now?** For each, capture:
   - a **stable contract id** (`^[A-Za-z0-9][A-Za-z0-9._-]*$`, e.g. `ARCH-Domain-Isolation`),
   - a short **title**,
   - a one-line **prose** boundary statement (what it owns and what it must never do),
   - an optional **scope glob** for the arch note (e.g. `src/Domain/**`).

## Guidance

- Seed **at most 2** contracts. More boundaries can be added later one at a time via `/can`.
- Everything is born `draft` (warn-only). The human reviews and locks incrementally.
- If the human is unsure about a boundary, skip it — a missing contract is better than a wrong
  locked one.

## Output (seed-spec JSON)

Write the answers to a temporary JSON file with this shape, then run
`New-ArchSeed.ps1 -TargetRoot <repoRoot> -SeedSpecPath <file>`:

```json
{
  "project": "MyApp",
  "boundaries": [
    {
      "id": "ARCH-Domain-Isolation",
      "title": "Domain isolation",
      "prose": "The Domain layer owns business rules and must never reference Api or Infrastructure.",
      "scope": "src/Domain/**"
    }
  ]
}
```

`boundaries` must have 1–2 entries. The script rejects 0 or more than 2.

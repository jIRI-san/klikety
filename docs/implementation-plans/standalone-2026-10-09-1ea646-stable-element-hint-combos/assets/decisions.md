# Proposed decisions

- Parent-side metadata memory survives helper retirement without keeping UIA alive.
- Reuse cacheWindowCount as the retention bound; no extra setting or migration.
- Explicit sparse slots decouple combos from discovery/list order.
- Prefer exact matches and occasional resets over heuristic identity recovery.
- Remember one activation only; accept loss of unseen partial-scan assignments.
- Reuse unchanged groups; do not preserve or reconstruct changed hierarchies.
- Preserve current discovery, adaptive labels, action validation and helper limits.
- User approved committing the reviewed plan and implementing it. Native manual
  acceptance remains pending; no app restart or merge/push is requested.

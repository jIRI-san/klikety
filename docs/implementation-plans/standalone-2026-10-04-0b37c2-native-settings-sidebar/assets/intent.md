# Intent

## Goal

Add production UI editing and saving on top of Klikety's JSONC configuration, using
the selected native WPF category-sidebar direction. This is a plan, not authorization
to implement, publish, merge, or remove the retained workshop prototype.

## Desired outcome

A reusable settings window opened from the actual tray, with common controls first,
advanced controls collapsed, seven categories, complete user-editable config coverage,
draft editing and recoverable Save & apply. The selected 850 x 650 DIP layout is the
starting point, not a fixed size that overrides accessibility/work-area adaptation.

### Selected operator wording and confirmed interpretation

- Source/date: coordinating parent's verbatim user-directed selection, 2026-10-04:
  "demo looks good, i think we can continue with that and create full impl plan for this directions".
  Interpretation: select Workshop concept 1 and create a production plan; not production
  implementation approval and not confirmation of every prototype shortcut.
- Source/date: this session's scope question, 2026-10-04.
  Operator wording: "Yes: full config.json editing; keep separate data and runtime controls outside Settings (Recommended)".
  Interpretation: expose all user-editable ConfigModel values, including logging,
  action/axis bindings, scroll, macro options/playback indicator, HUD appearance.
  Exceptions: configVersion/$schema read-only; theme-file contents, recordings,
  startup registry and HUD runtime activation remain in existing interfaces.
- Source/date: this session's failure-semantics question, 2026-10-04.
  Operator wording: "2. Restore previous file/runtime where possible; surface recovery failure (Recommended)".
  Interpretation: attempt guarded recovery on candidate activation failure;
  recovery is not guaranteed against Windows resource loss or later external edits.
- Source/date: this session's JSONC contract question, 2026-10-04.
  Operator wording: "Confirm preservation and conflict detection with the disclosed external-writer race (Recommended)".
  Interpretation: retain comments, unknown fields, untouched value text and backup;
  normalize changed scalar/new fragment text; reject observed external edits.
  Exception: optimistic last-check/replace race against noncooperating writers accepted.
- Source/date: this session's binding UX question, 2026-10-04.
  Operator wording: "Pickers plus focused key/shortcut capture; validate before save (Recommended)".
  Interpretation: physical VKey identity, layout-aware labels, focused capture without
  a new global capture hook; ordered axis/slot lists and action rows are editable.
- Source/date: this session's acceptance-boundary question, 2026-10-04.
  Operator wording: "Confirm these boundaries and measurable keyboard/DPI checks (Recommended)".
  Interpretation: diagnostics under General > Advanced; older configs pass through
  existing migration; malformed/future config produces a repair message, not defaults
  written by Settings. Keyboard-only workflows across every page; controls not clipped
  at 100/150/200% on a logical work area of at least 1280 x 720 DIP; accessible names.
- Source/date: this session's historical-context question, 2026-10-04.
  Operator wording: "look into old plans, however use them more as a advice of what the intent was, the implementation is authoritative".
  Interpretation: archived intent informs understanding, does not override current code.
- Source/date: this session's collection/runtime boundary question, 2026-10-04.
  Operator wording: "Confirm comment retention, idle-only apply and conflict-safe rollback (Recommended)".
  Interpretation: removed-entry comments survive as standalone section comments,
  not necessarily attached to a key; reject save/apply while navigation/recording/playback/
  picker is active, retain draft; rollback refuses to replace a newer external file.

### Review-selected compatibility decisions

- Source/date: consolidated review selection, 2026-10-04. Operator wording:
  "Apply clarifications/simplifications; decide 15 and 18 separately (Recommended)".
  Interpretation: apply the listed plan clarifications and scalar-first sequencing;
  leave already-covered findings unchanged; decide compatibility separately.
- Source/date: finding 15 question, 2026-10-04. Operator wording:
  "Preserve current compatibility: show explicit warnings and effective behavior, do not add length-based save blockers (Recommended)".
  Interpretation: LogGrid short/extra axes and macro slot list lengths retain current
  warning/effective-runtime behavior; no new length blocker. Macro speed finite and >=0;
  zero keeps its existing fixed-delay meaning.
- Source/date: finding 18 question, 2026-10-04. Operator wording:
  "Use existing schema bounds for edited indicator values; preserve untouched legacy values and surface incompatibility (Recommended)".
  Interpretation: edited indicator radii >=1, duration >=100 ms, other edited values
  follow current schema plus consumer safety. Untouched legacy values are not silently
  rewritten or rejected solely for stricter schema floors; unsafe runtime values still
  receive explicit blocking/safety handling. No schema relaxation authorized.

## Success signals

- Every current user-editable config value has a named editor; metadata stays read-only.
- Save/reopen and runtime application agree; failed apply attempts recovery and explains
  actual disk/runtime state without a success-shaped fallback.
- Keyboard-only and DPI checks exercise the real window, not an isolated settings mock.

## Non-goals

- Theme content editor, macro recording/data editor, startup/HUD runtime switches.
- New UI framework, broad unrelated coordinator/refactoring work, new configuration version
  solely for UI, automatic merge of external edits, strict exclusion of external writers.
- Guaranteed rollback after resource/process/power failure; guaranteed recovery from an
  initially invalid/nonworking runtime. Recovery preserves and reports that initial state.
- Reproducing a prior active navigation/macro session after save; apply starts only at idle.

### Delegated discretion and deferred choices

- Implementation may replace prototype control-building with small typed draft/page helpers.
  Fixed: native WPF, sidebar, common-first/advanced, unchanged existing runtime semantics.
- Implementation chooses local patch algorithms/control layouts within confirmed guarantees;
  no root ConfigModel serialization or discarded comments to make implementation easier.
- If JSONC collection edits cannot meet preservation, the implementer stops for the user;
  no implicit weaker guarantee. If recovery cannot be verified, stop before runtime integration.
- Remaining detailed label/spacing choices belong to the implementer, verified against the
  explicit keyboard/DPI checks; no additional scope or separate data editors are authorized.

## Definition of done

All requirements have implementation evidence, focused regressions and live isolated
runtime/layout checks; related design notes describe production behavior and limitations.
Existing prototype checks are starting evidence, not completed production plan steps.
Implementation permission remains a later user decision.

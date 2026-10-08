# Domain Model

## Terms and meanings

- Document snapshot: exact config bytes, encoding/BOM and parse spans loaded for a draft.
- Draft: editable known settings plus tracked changes; not the running init-only ConfigModel.
- Candidate: validated config and patched document prepared for one Save & apply.
- Apply: activation of candidate services/renderers/registrations at an idle boundary.
- Recovery: conditional restoration of previous bytes and prior effective runtime settings.
- Recovery baselines: previous disk bytes/model and previous active runtime config/toggles
  are independent snapshots, not assumed identical.
- Idle: no active navigation overlay, macro recording/playback/picker or apply operation.
  HUD being enabled is not busy; scroll pause is a runtime state to preserve.
- Blocking error versus advisory warning: explicit typed outcome, not string matching.

## Actors and boundaries

- User edits draft and owns Save/Discard/Close decisions.
- External editor may change config independently; Settings detects observed changes.
- Settings owns config.json edits; ThemeLoader reads separate themes; MacroStore owns
  macros.json; StartupRegistryService owns startup; tray owns runtime HUD enablement.
- Windows owns hotkey/hook availability; successful preflight does not reserve availability.

## Interfaces and ownership

- SettingsWindow/pages: accessible controls, focus, dirty/errors; no filesystem/runtime logic.
- Typed settings draft/session: changes, field mapping, validation, snapshot state.
- ConfigLoader/shared validators: startup-compatible parsing/defaults/binding constraints;
  strict settings input errors do not silently substitute defaults.
- SettingsConfigStore: token-aware insert/update/delete, collection operations, optimistic
  version check, same-volume atomic replacement/backup, guarded recovery.
- App composition root and a small apply helper: idle guard, teardown/activation,
  service ownership, recovery result and tray consistency. No general DI platform needed.
- Existing services are reused; injectable file/apply seams support fault tests.
- App supplies immutable AppPaths to every file-owning component; fixture mode confines
  config/themes/logs/macros/extraction/reset/topology without changing production paths.

## Invariants

- Metadata/unknown fields are outside editable known settings; no accidental overwrite.
- Physical key identity and ordering survive label/layout changes.
- No file replacement on validation error, observed conflict, malformed/future input or busy apply.
- Preserve config comments even for edited collections; orphan comment association is not promised.
- Registration/logging/HUD failures are distinguishable from advisory macro/theme/config warnings.
- Privacy contract: key capture/HUD content not written to logs; input capture stays focused.
- Plan-only session edits plan artifacts; production execution is not underway.

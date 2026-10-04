# 0b37c2: Native Settings Sidebar
<!-- plan-id: 0b37c2 -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:80b78524ded3486fa80a0dec5a7ada7a9de1855eb469f976d8c2819871fae66d -->
<!-- execution-mode: manual -->
<!-- scope: step -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

- Intent - [assets/intent.md](assets/intent.md)
- Domain model - [assets/domain.md](assets/domain.md)
- Proposed design - [assets/design.md](assets/design.md)
- Requirements - [assets/requirements.md](assets/requirements.md)
- Risks - [assets/risks.md](assets/risks.md)
- Decisions - [assets/decisions.md](assets/decisions.md)
- References - [assets/references.md](assets/references.md)

All steps are remaining production work. Prototype code/checks may be reused after
verification; none of these steps is completed by Workshop approval.

## Phase 1: MVP document-to-draft slice

- [x] 1.1 Build strict document validation and scalar JSONC saves (REQ-3, REQ-4, RISK-1, RISK-3, RISK-4) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** A candidate can be parsed, validated, previewed and atomically saved against
  a snapshot without replacing malformed input, unknown fields, comments or untouched text.
  Scope here is scalar/nested-object edits; collection operations arrive before binding UI.

  **Likely touchpoints:** ConfigLoader, ConfigMigrator, SettingsConfigStore, shared validation
  helpers, existing schema/models, SettingsConfigStoreTests/SettingsValidationTests.

  **Constraints:** Reuse current defaults/rules; keep init-only model. Existing migration
  precedes editable snapshot, no implicit migration during preview/commit. Block unsupported
  input and observed external conflicts. Retain orphan comments, BOM and previous bytes.
  Distinguish save-blocking validation from current advisory warnings.

  **Verify:** Strict parse/null/duplicate/version/migration fixtures, scalar updates and
  scalar insertion tests, all nested-object defaults, BOM/comments/unknown byte checks,
  no-op/conflict/file-deleted/access/atomic-replace fault tests.

  **Stop/escalate when:** Preservation/default semantics cannot be met, or a newly imposed
  validation rule requires unrelated runtime compatibility changes; bring specific conflict
  to the user instead of silently round-tripping the model or dropping comments.

  </details>
- [ ] 1.2 Establish native tray-to-typed-draft General editor (REQ-1, REQ-7) [after: 1.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** One reusable native sidebar window edits activation settings through a
  testable typed draft. The session owns snapshot and General draft; other categories
  are navigation shells until their steps, preserving untouched values in the snapshot.

  **Likely touchpoints:** SettingsWindow.xaml/code-behind, small Settings draft/page helpers,
  App.ShowSettings/tray wiring, SettingsDraftTests/SettingsWindowTests.

  **Constraints:** Common-first and collapsed Advanced; accessible WPF labels/focus/status;
  page switches retain draft, changed-back values clear dirty state; close/Discard confirm.
  No new framework, no fake working controls, no file I/O inside page control logic.

  **Verify:** Draft dirty/revert/page switch/error retention tests; actual tray reuse;
  keyboard edit/Discard/close-cancel checks on General with fixture config.

  </details>

## Phase 2: MVP real save, apply and recovery

- [ ] 2.1 Make runtime composition return typed apply/recovery outcomes (REQ-5, RISK-2, RISK-3, RISK-5, RISK-9) [after: 1.1] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** Given a validated captured config, attempt idle-only runtime activation,
  tear down partial candidates and attempt guarded previous-file/runtime recovery.

  **Likely touchpoints:** App.BootstrapCoordinator/ReloadConfiguration, small settings-apply
  helper/seams, hotkey/scroll/macro/HUD/logger lifetimes, NavigatorCoordinator idle state,
  immutable AppPaths supplied to config/themes/logs/macros/extraction/reset/topology stores.

  **Constraints:** Serialize Save/Reload/Reset, reject active navigation/macro work before
  disk mutation. Owner-aware preflight does not mistake current registrations for external
  conflicts. Use captured old/candidate models, not startup fallback or a moving reread.
  Preserve HUD enabled and scroll paused state. Separate advisories from activation failure.
  Restore old bytes only when disk still matches candidate; preserve original backup;
  report diverged disk/runtime and recovery failure. No unrelated coordinator overhaul.
  Capture previous disk bytes/effective model separately from previous runtime config/
  toggles. Explicit captured-model composition does not call ConfigLoader.Load during
  apply/recovery. Independent candidate resource ownership, reverse disposal, success-only
  ownership swap; keep prior logger alive through teardown/recovery/outcome reporting.
  Inventory main/scroll/macro shortcuts, honor retained ownership, block candidate collisions,
  defer owned-key reassignment to controlled activation without unregister-for-probe.
  Restore via separate temp/sidecar replacement that cannot overwrite .settings.bak.

  **Verify:** Fake service/file tests for each main/scroll/macro registration, logging/HUD
  creation and partial-resource failure; recovery success/failure/external conflict;
  busy guards, serialization, runtime toggle preservation, no duplicate handlers/hooks.
  Include initially divergent disk/runtime snapshots and independent restoration results.

  **Stop/escalate when:** Current composition cannot expose or clean a resource safely, or
  recovery tests report success despite a partial failure. Stop before real integration;
  do not claim a guaranteed rollback.

  </details>
- [ ] 2.2 Connect Save & apply end-to-end and retain drafts on failure (REQ-1, REQ-5, RISK-2, RISK-5) [after: 1.2, 2.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** General Save & apply runs validation/preflight/commit/activation/recovery
  through the actual tray path and displays accurate disk/runtime result.

  **Likely touchpoints:** Settings draft/session, SettingsWindow status/actions, App apply
  integration, SettingsApplyTests/SettingsRecoveryTests.

  **Constraints:** Successful apply rebases clean draft. Save failure retains all edits;
  failed apply/recovery preserves editable candidate plus correct loaded snapshot and
  targeted retry/reload instructions. Quit/close cannot silently discard. Error text is
  user-visible and loggable without captured key content.
  Never LoadDraft/rebase before full success. Keep candidate edits, disk base and active
  runtime separate; after recovery rebase edits onto restored base, while newer external
  bytes require explicit reload/conflict resolution before retry.

  **Verify:** General edit/save/reopen; injected validation/I/O/apply/recovery failures;
  action enabled/disabled and retry state tests; tray/runtime consistency.

  </details>

## Phase 3: Complete navigation, binding and appearance editing

- [ ] 3.1 Implement comment-aware collections, key editors and capture (REQ-2, REQ-3, REQ-4, REQ-6, REQ-7, RISK-1, RISK-6) [after: 2.2] `L`
  <details><summary>Implementation contract</summary>

  **Outcome:** Key bindings page edits action rows and ordered axis lists with readable
  pickers/capture and precise collision validation; helpers are reusable by other pages.

  **Likely touchpoints:** key control/draft helpers, Key bindings page, key label resolver,
  shared validation, SettingsKeyEditorTests/SettingsRoundTripTests.

  **Constraints:** Stable physical VKey, no new global capture hook; Escape/focus loss cancel,
  modifier-only/repeats handled, unsupported/system-reserved capture uses picker fallback.
  Action add/remove includes implicit Space explanation. Collection ordering/comments
  follow document contract; do not rewrite unedited arrays.
  First implement/test byte spans for containers, members/elements, delimiters and
  comments with add/move/delete. Deleted-member comments stay inside the same object;
  deleted-element comments stay inside the same array, without promised reattachment.
  Then wire collection controls; all six current MouseAction values are available.

  **Verify:** Capture/keyboard/layout/repeat tests; action and ordered list add/move/remove
  save/reopen; reserved/duplicate/full collision rejection, unchanged-file assertions.

  **Stop/escalate when:** Focused capture requires claiming global/system shortcuts or
  collection editing loses comments; retain picker option, escalate specific requirement gap.

  </details>
- [ ] 3.2 Complete Navigation and app-scope editors (REQ-2, REQ-3, REQ-6, REQ-7, RISK-4) [after: 3.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** All four mode flags, default/chords, optional scope, mode sizes and level-3
  threshold save/reopen and drive existing navigation behavior.

  **Likely touchpoints:** Navigation page/draft, ConfigLoader mode validation,
  NavigatorCoordinator.BuildChordKeyMap, mode factory and focused coordinator tests.

  **Constraints:** Exactly one enabled default; explicit per-mode two-key/arrow rules;
  unique chords for enabled non-defaults, including Uniform grid; disabled/inert values
  retained/editable in Advanced with applicability help. Preserve non-QWERTY fallback.
  Enabled UniformGrid requires twoKey || arrowKeys; the other enabled modes require
  twoKey=true with arrows optional. On non-QWERTY layouts, non-Uniform default falls back
  to UniformGrid and non-Uniform chords are ignored without rewriting configuration;
  preserve current one-warning logging. All four modes expose both size fields in
  Advanced, marking inert values. Reconcile config.schema.json logBaseSize default to 10.

  **Verify:** Default switch and Uniform grid chord regression; enable/disable/nullable
  scope, each size field round-trip; impossible combinations block without file writes.

  </details>
- [ ] 3.3 Complete Appearance and General diagnostics (REQ-2, REQ-3, REQ-7, RISK-4) [after: 2.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Built-in/custom theme reference and label floor plus all logging values
  are editable; config path/version/schema visible read-only.

  **Likely touchpoints:** General/Appearance drafts/pages, ThemeLoader, LoggingSetup,
  SettingsCoverageTests/SettingsValidationTests.

  **Constraints:** No theme content/registry editor; General diagnostics collapsed.
  Reject invalid references/numbers/enum choices clearly; do not replace a custom theme
  with dark due to a loader fallback or silently coerce log-level text.
  Changed theme reference resolves/parses; unchanged existing fallback and external
  invalid-color warnings stay advisory. Named LogLevel and retained count >=1 in Settings;
  startup fallback unchanged outside Settings.

  **Verify:** Custom/built-in theme, numeric floor, diagnostics round-trip; theme load
  and logger I/O failure through recovery; metadata unchanged byte assertions.

  </details>

## Phase 4: Complete remaining configured subsystems

- [ ] 4.1 Implement Scrolling page and actual apply behavior (REQ-2, REQ-3, REQ-5, REQ-6, REQ-7) [after: 3.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Enabled/up/down/amount settings save and apply through ScrollHotKeyService.

  **Likely touchpoints:** scrolling draft/page, shared key control, scroll validation/service,
  settings round-trip/apply tests.

  **Constraints:** Pause remains runtime tray state; preserve prior pause when still enabled,
  explain disabled transition; no mouse events from draft editing. Existing range/collisions.

  **Verify:** Enable/disable and hotkey/amount round-trip, duplicate collision rejection,
  fake registration/recovery tests, preserved pause and tray menu behavior.

  </details>
- [ ] 4.2 Implement macro options and playback indicator controls (REQ-2, REQ-3, REQ-5, REQ-6, REQ-7, RISK-4) [after: 3.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Macros config, nullable global hotkey, record/helper/ordered slot keys,
  speed and all playbackIndicator values are editable and applied.

  **Likely touchpoints:** macro draft/page, shared key/list controls, macro validation,
  MacroHotKeyService/indicator composition, SettingsRoundTripTests.

  **Constraints:** No recordings/macros.json write, no startup hook activation by preview.
  Existing collision/slot/speed behavior authoritative; newly exposed visual values
  follow the user-selected schema/legacy compatibility boundary plus consumer safety.
  Slot list lengths keep existing warning/effective-runtime behavior, no exact-ten
  blocker; speed finite and >=0, zero keeps current fixed delay. Edited indicator
  radii >=1/duration >=100 ms per existing schema, with finite/color/thickness safety.
  Untouched legacy values remain with explicit incompatibility warnings; do not rewrite
  or block solely on stricter schema floors. Unsafe runtime construction remains a
  named failure, not silent fallback. Missing playbackIndicator uses effective defaults.

  **Verify:** Null/enabled/hotkey/list/options/indicator round-trip; invalid and disabled
  field cases; separate macro-file warning remains advisory; apply/recovery and busy guards.

  **Stop/escalate when:** Correcting an existing consumer quirk changes valid stored
  behavior outside the settings feature; present that compatibility decision explicitly.

  </details>
- [ ] 4.3 Implement HUD appearance page and active HUD refresh (REQ-2, REQ-3, REQ-5, REQ-7) [after: 2.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Every HUD visual/timing value saves and updates an already enabled HUD;
  disabled HUD remains off.

  **Likely touchpoints:** HUD draft/page, existing HUD validation/display lifecycle,
  App runtime apply, SettingsRoundTripTests/SettingsApplyTests.

  **Constraints:** No JSON/runtime enable toggle; no captured key content persisted/logged.
  Shared color/range checks and accessibility names, no new hook while editing.

  **Verify:** All ten HUD fields, invalid color/corner/ranges, active/off apply and
  failure recovery; no leaked extra hook/old window and tray state matches actual HUD.

  </details>

## Phase 5: Verify complete coverage, safety and native usability

- [ ] 5.1 Verify full configuration coverage, isolation and fault matrix (REQ-2, REQ-3, REQ-4, REQ-5, REQ-8, RISK-1, RISK-2, RISK-3, RISK-8) [after: 3.1, 3.2, 3.3, 4.1, 4.2, 4.3] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** A known-field coverage table and targeted tests establish editor/round-trip
  completeness and file/runtime behavior across handled errors.

  **Likely touchpoints:** SettingsCoverageTests/SettingsRoundTripTests/SettingsIsolationTests,
  settings store/apply/recovery test fixtures, native isolated runtime test seam.

  **Constraints:** All automated mutable paths are temporary fixtures, including logs,
  macros/themes/topology. No user's real AppData or registry writes; no broad suite
  required unless focused regressions reveal a shared behavior issue.

  **Verify:** Each known field explicit in coverage; full round-trip + unknown preservation;
  file/registration/logger/HUD faults and guarded recovery; supported migration, busy,
  external edit, no-op, repeated save/reopen/resource reuse; smallest Release build/tests.

  **Stop/escalate when:** Isolation cannot encompass every mutable resource or unique
  hotkeys cannot be arranged; do not start a runtime fixture that interferes with user app.

  </details>
- [ ] 5.2 Verify keyboard/accessibility and scaled native layouts (REQ-1, REQ-6, REQ-7, RISK-6, RISK-7) [after: 5.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** All pages usable with keyboard, named automation controls, announced
  errors/status, and no clipped actionable controls in the confirmed DPI matrix.

  **Likely touchpoints:** SettingsWindow/page/control XAML, SettingsWindowTests and
  SettingsAccessibilityTests, existing native smoke infrastructure where applicable.

  **Constraints:** Logical work area >=1280x720 DIP at 100/150/200%; scroll and footer
  access required; no simulated screenshots claimed as actual native DPI evidence.

  **Verify:** Tab/Shift+Tab/category arrows, capture cancel, collection edit, Advanced,
  error navigation, save/discard/close; automation names/focus and layout bounds at
  each scaling. Record unsupported live rows for the human verification step.

  </details>
- [ ] 5.3 Exercise actual isolated native registration/apply/recovery and layout matrix (REQ-5, REQ-7, REQ-8, RISK-2, RISK-7, RISK-8) @human [after: 5.1, 5.2] `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Review fixture path isolation and unique test hotkeys; use an execution host/display
     where the running user Klikety is not affected. Stop if isolation is not established.
  2. Launch the actual runtime fixture, not only hook-free --settings-demo. Open tray
     Settings; edit each category, save/reopen, verify activation, scrolling, macro hotkey,
     theme/logger behavior and enabled HUD refresh/off behavior.
  3. Create an intentional test-hotkey conflict and file/runtime failure using fixture
     controls; verify explicit error, recovery and unchanged newer external edit.
  4. Use keyboard-only workflow on each page; inspect 100/150/200% at the confirmed
     logical work area; mark every unverified row rather than calling it passed.

  **Verify:** Successful values match disk/runtime; busy rejects without file change;
  invalid binding rejects; recovery states and registration cleanup are observable;
  one Settings window, clean key capture, focus/status and unclipped action controls
  for each matrix row. Any unavailable mandatory row blocks completion.

  **Rollback:** Close/stop only the identified fixture process; release its test
  registrations and restore its fixture backup. Do not delete worktrees, alter the
  user's configuration or stop the user's app.

  </details>

## Phase 6: Document production behavior

- [ ] 6.1 Update matching design notes and user-facing settings guidance (REQ-8) [after: 5.3] `S`
  <details><summary>Implementation contract</summary>

  **Outcome:** Production entrypoints/coverage, draft workflow, JSONC guarantees/race,
  repair/conflict/recovery actions and runtime-only boundaries are documented.

  **Likely touchpoints:** docs/design-notes/settings.design.md and root index;
  config/navigation/grid/testing/HUD/macros notes and README settings guidance.

  **Constraints:** Preserve prototype identity as exploratory history, not production
  proof. No unrelated docs rewrite, new release/merge requirement or auto-cleanup.

  **Verify:** Coverage matches actual editors and tests; examples use correct AppData
  path/runtime ownership; links resolve and limits/recovery failure are explicit.

  </details>

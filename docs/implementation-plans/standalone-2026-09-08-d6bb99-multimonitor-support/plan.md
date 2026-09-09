# d6bb99: Multimonitor support
<!-- plan-id: d6bb99 -->
<!-- cip-stage: drafted -->
<!-- planning-confirmed: sha256:67b145e2c59212b8392dac41877beb4d81b0b45de2a2aae69562bd37d2dd211d -->
<!-- Folder naming: <epic-id|standalone>-<yyyy-mm-dd>-<6hex>-<slug> · plan-id is the canonical handle (date/slug/hash all resolve via Resolve-Plan). New-Plan.ps1 fills these in. -->

<!-- Optional execution metadata — defaults used by /ci mode selection -->
<!-- execution-mode: manual -->
<!-- scope: step -->
<!-- evidence: required -->
<!-- phase-budget-points: 6 -->
<!-- expected-packages: none -->

## Assets

`plan.md` holds only the markers above, this index, and the phases/steps below. Everything else lives under `assets/` and is loaded on demand — never wholesale.

- Intent — [assets/intent.md](assets/intent.md)
- Domain model — [assets/domain.md](assets/domain.md)
- Approved design — [assets/design.md](assets/design.md)
- Requirements — [assets/requirements.md](assets/requirements.md)
- Risks — [assets/risks.md](assets/risks.md)
- Decisions — [assets/decisions.md](assets/decisions.md) (extended rationale in `assets/decisions/<topic>.md`)
- References — [assets/references.md](assets/references.md)
- Review results — advisory `assets/reviews/phase-<N>.md` and `assets/reviews/final.md`
- AI-credit ledger — `assets/ai-credits.json` (created by autonomous execution)

A subfolder is created only when a concern needs more than one file (`assets/decisions/`, `assets/logs/`); single-file concerns stay flat under `assets/`.

## Phase 1: Display identity and numbering
<!-- worktree: C:\Users\jiri\root\dev\copilot-worktrees\klikety\jiri-san-special-doodle -->
<!-- Steps with no [after:] annotation can start immediately and run in parallel. -->
<!-- Roles: @ai-agent (default, not annotated) or @human (explicit).
     Nontrivial AI steps carry a compact details block with Outcome, Likely touchpoints, Constraints,
     Verify, and—when uncertain or high risk—Stop/escalate when. Omit it for self-explanatory S work. -->
<!-- Sizes: S (< 30 min) · M (30 min – 2 h) · L (2 h+) -->
<!-- Point legend: S=1, M=2, L=3 (phase-budget cap comes from the phase-budget-points marker; default 6) -->

- [x] 1.1 Display catalog: enumerate monitors, match CCD DevicePath, fail closed (REQ-3, RISK-2, RISK-3) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** `IDisplayCatalog` returns a frozen list of active displays (physical `rcMonitor`, DPI, GDI name, DevicePath, virtual-screen rect, display containing a point) or a hard failure. No partial maps.

  **Likely touchpoints:** `src/Klikety/Services/IPlatformServices.cs`, new catalog types under `src/Klikety/Services/`, `src/Klikety/Interop/NativeMethods.cs` (`EnumDisplayMonitors`, `MONITORINFOEX`, `QueryDisplayConfig`, `DisplayConfigGetDeviceInfo`), `Klikety.Tests` fakes.

  **Constraints:** Identity key is CCD `monitorDevicePath`, not `HMONITOR` or `\\.\DISPLAYn`. Empty or duplicate DevicePath → failure. CCD active count ≠ `EnumDisplayMonitors` count → failure. Do not guess pairings.

  **Verify:** `test:DisplayNumberingTests.AssignsSpatiallyForUnknownFingerprint` uses injected catalog rows; failure-path tests cover empty/duplicate/count mismatch.

  **Stop/escalate when:** a standard extend topology on the operator machine reports empty DevicePath, duplicates, or CCD/monitor count mismatch.

  </details>
- [x] 1.2 Topology store: fingerprint, spatial 1..N, persist, cap at 9 (REQ-3, REQ-4, REQ-12, RISK-2) [after: 1.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Unknown fingerprint assigns 1..N by `(Left, Top, DevicePath)` and writes `%APPDATA%\Klikety\display-topologies.json`. Known fingerprint reuses numbers even if rects moved. Fingerprint ignores which display hosts navigation. Displays past 9 are unnumbered.

  **Likely touchpoints:** `src/Klikety/Services/DisplayTopologyStore.cs`, `DisplayNumberingTests`, `DisplayTopologyStoreTests`.

  **Constraints:** Not stored in `config.json`. Same DevicePath set after rearrange does not renumber. Partial unplug is a new fingerprint (no holes). Overlay-host switch must not rewrite numbers.

  **Verify:** `test:DisplayNumberingTests.ReusesMapForKnownFingerprint` · `test:DisplayNumberingTests.CapsAtNine` · `test:DisplayTopologyStoreTests.RoundTripsKnownFingerprint` · `file:src/Klikety/Services/DisplayTopologyStore.cs#exists`

  **Stop/escalate when:** DevicePath cannot be the store key (RISK-2 stop from 1.1).

  </details>
- [ ] 1.3 Catalog failure unit tests (REQ-3, RISK-2, RISK-3) [after: 1.1] `S`

## Phase 2: Virtual-desktop mouse, DPI, activation
<!-- worktree: (recorded by /ci when worktree is created) -->

- [ ] 2.1 Virtual-desktop `SendInput` normalization (REQ-8, RISK-5) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** `MouseActionService` maps physical pixels through virtual-screen metrics with `MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`. Clicks work when virtual origin is negative.

  **Likely touchpoints:** `src/Klikety/Services/MouseActionService.cs`, `NativeMethods` (`GetSystemMetrics` SM_XVIRTUALSCREEN/Y/CX/CY), `MouseNormalizationTests`, smoke `MoveTo`.

  **Constraints:** Inject or wrap virtual-screen rect for unit tests. Macros still use `IScreenBoundsProvider` primary size (non-goal). Do not keep primary-relative 0–65535 mapping.

  **Verify:** `test:MouseNormalizationTests.NegativeVirtualOrigin` · `file:src/Klikety/Services/MouseActionService.cs#contains:MOUSEEVENTF_VIRTUALDESK`

  **Stop/escalate when:** a click on a display whose origin is negative lands on the wrong display.

  </details>
- [ ] 2.2 Per-Monitor V2 manifest (REQ-1, RISK-4) `S`
- [ ] 2.3 Platform catalog + activate from any display (REQ-9) [after: 1.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** `IPlatformServices` exposes the catalog. Hotkey activation uses the display containing the cursor. Primary-only `Contains` suppress is gone. Single-display path still shows one overlay.

  **Likely touchpoints:** `IPlatformServices`, `FakePlatformServices`, `NavigatorCoordinator.OnHotKeyActivated`, `CoordinatorDisplaySwitchTests`, existing coordinator tests whose cursor was forced inside primary.

  **Constraints:** Session `ScreenBounds` is that display’s `rcMonitor`, not primary. `IMonitorService` stays HUD-only.

  **Verify:** `test:CoordinatorDisplaySwitchTests.ActivatesWhenCursorOnSecondary`

  </details>

## Phase 3: Overlay host and display switch
<!-- worktree: (recorded by /ci when worktree is created) -->

- [ ] 3.1 OverlayHost: one nav window plus satellites, no focus steal (REQ-1, RISK-1) [after: 2.2, 2.3] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Two-or-more displays → one WPF window per display. Satellites shown first, never `Activate()`. Nav overlay uses existing `IOverlayWindow.Show(Rectangle)`. Focus-lost switching guard covers host show/hide. Single display: no satellites.

  **Likely touchpoints:** `src/Klikety/Overlay/OverlayHost.cs`, `SatelliteWindow`, `OverlayWindow` styles, `App.xaml.cs` wiring, `NavigatorCoordinator` deactivate/hide, `OverlayHostTests`.

  **Constraints:** Satellites are not `IOverlayWindow`. `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT`. Windows sized to that display’s physical bounds via that window’s `PresentationSource`. DIP size must match `GetDpiForMonitor` (RISK-4).

  **Verify:** `test:OverlayHostTests.ShowsOneWindowPerDisplay` · `file:src/Klikety/Overlay/OverlayHost.cs#exists`

  **Stop/escalate when:** showing satellites dismisses the nav overlay on a two-display machine.

  </details>
- [ ] 3.2 Satellite digit rendering (REQ-2) [after: 3.1] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Non-nav displays show a centered outlined digit using theme label fill/outline. Font size = 40% of min(DIP width, DIP height), clamped 96–400.

  **Likely touchpoints:** `SatelliteWindow`, theme brushes, `GridRenderer` outlined-text helper (reuse, do not fork a third outline stack if a shared helper already exists), `SatelliteWindowTests`.

  **Constraints:** Unnumbered displays (10+) show no digit and are not clickable. No click-to-select.

  **Verify:** `test:SatelliteWindowTests.FontSizeIsFortyPercentClamped` · `file:src/Klikety/Overlay/SatelliteWindow.cs#exists`

  </details>
- [ ] 3.3 Digit dispatch and switch reset (REQ-4, REQ-5, REQ-6) [after: 3.1, 1.2] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** While overlay visible, D1–D9 run before `MacroHandler`. Other numbered display → move nav, new L1 same mode, cursor at target center, cancel app-scope and drag, rebuild satellites, numbers unchanged. Own number no-op. Unused digit consumed, not forwarded to the session.

  **Likely touchpoints:** `NavigatorCoordinator.OnKeyEvent`, `SessionManager`, `ActionDispatcher` drag cancel, `OverlayHost`, `CoordinatorDisplaySwitchTests`.

  **Constraints:** Numbers do not rewrite the topology store on switch. Keep current mode name, not the configured default.

  **Verify:** `test:CoordinatorDisplaySwitchTests.DigitMovesNavOverlay` · `test:CoordinatorDisplaySwitchTests.OwnDigitIsNoOp` · `test:CoordinatorDisplaySwitchTests.SwitchResetsToL1SameMode` · `test:CoordinatorDisplaySwitchTests.SwitchCancelsAppScopeAndDrag` · `test:OverlayHostTests.SwitchDoesNotRenumber`

  </details>

## Phase 4: App-scope, slot keys, display-change
<!-- worktree: (recorded by /ci when worktree is created) -->

- [ ] 4.1 App-scope clips to active nav display (REQ-10) [after: 3.3] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** App-scope intersection uses the navigation display’s `rcMonitor`. Empty intersection → existing status flash and stay full-display.

  **Likely touchpoints:** `NavigatorCoordinator` app-scope chord handler, `AppScopeCoordinatorTests`.

  **Constraints:** Display switch already cancelled app-scope (3.3). Spanning windows remain clipped to one display.

  **Verify:** `test:AppScopeCoordinatorTests.ClipsToActiveNavDisplay` · `test:AppScopeCoordinatorTests.EmptyIntersectionStaysFullDisplay`

  </details>
- [ ] 4.2 Config v7: F1–F10 slot keys, reserve D1–D9 (REQ-11) `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** `VKey` includes F1–F10. Defaults and embedded `config.json` use F1–F10. Version 6→7 migrates only exact D0–D9. Custom slot keys kept. D1–D9 rejected in slotKeys, horizontalKeys, verticalKeys, actionBindings, chord keys.

  **Likely touchpoints:** `VKey.cs`, `ConfigModel`, `ConfigMigrator` (`CurrentConfigVersion` 7), `ConfigLoader` reserved set, `config.json` / schema, `ConfigMigratorTests`, `ConfigLoaderTests`.

  **Constraints:** Do not rewrite custom `slotKeys`. D0 is not a display key.

  **Verify:** `test:ConfigMigratorTests.MigratesDefaultSlotKeysToFKeys` · `test:ConfigMigratorTests.PreservesCustomSlotKeys` · `test:ConfigLoaderTests.RejectsDigitDisplayKeysInSlotKeys` · `file:src/Klikety/Resources/config.json#contains:"F1"`

  </details>
- [ ] 4.3 `WM_DISPLAYCHANGE` dismisses overlay (REQ-7) [after: 3.1] `S`

## Phase 5: Docs, integration, manual
<!-- worktree: (recorded by /ci when worktree is created) -->

- [ ] 5.1 Update design notes and index (REQ-1) [after: 3.1, 4.2] `S`
- [ ] 5.2 Integration tests and mixed-DPI DIP check (REQ-1, REQ-2, REQ-4, REQ-5, REQ-6, REQ-8, RISK-1, RISK-4) [after: 3.3, 4.1, 4.2, 4.3] `M`
  <details><summary>Implementation contract</summary>

  **Outcome:** Coordinator/host tests cover multi-display activate, switch, no-renumber, virtual-desk flag present. Overlay DIP vs `GetDpiForMonitor` documented as the RISK-4 stop; unit tests use injected DPI.

  **Likely touchpoints:** `CoordinatorDisplaySwitchTests`, `OverlayHostTests`, `MouseNormalizationTests`, `testing.design.md` fakes.

  **Constraints:** No real Win32 in unit tests. Smoke tests stay `[Trait("Category","Smoke")]`.

  **Verify:** the REQ-1/2/4/5/6/8 test markers above plus `file:src/Klikety/Services/MouseActionService.cs#contains:MOUSEEVENTF_VIRTUALDESK`

  **Stop/escalate when:** overlay DIP size ≠ physical / that display’s DPI (tolerance 1 DIP) on mixed-DPI hardware.

  </details>
- [ ] 5.3 Two-display manual check (REQ-1, REQ-2, REQ-4, REQ-5, REQ-6, REQ-8, RISK-1, RISK-4) @human [after: 5.2] `M`
  <details><summary>Details</summary>

  **Steps:**
  1. Extend to two displays (ideally mixed DPI). Run the built Klikety from this branch.
  2. Put the cursor on display 1, activate. Confirm nav grid on 1 and a large `2` on the other.
  3. Press `2`. Confirm nav moves, cursor recenters, numbers do not swap, session is L1 in the same mode.
  4. Press `2` again (own number): no change. Click a cell on the new display: click lands there.
  5. Unplug or Win+P while overlay is visible: overlay dismisses.
  6. Reconnect the same set, activate: numbers match step 2.

  **Verify:** Steps 2–6 match. Showing the satellite did not dismiss nav (RISK-1). Overlay window DIP size matches that display’s DPI (RISK-4).

  **Rollback:** Use previous release / revert the branch. Delete `%APPDATA%\Klikety\display-topologies.json` if a bad map was written.

  </details>

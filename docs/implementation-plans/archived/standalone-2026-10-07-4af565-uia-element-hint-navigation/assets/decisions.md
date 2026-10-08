# Decisions

## User-confirmed choices, 2026-10-07

- **Application scope:** foreground application root only. Exclude other top-level windows; clip to the active navigation display/current scope.
- **Failure isolation:** separate owned helper with a hard timeout. No in-process soft-timeout substitute.
- **Interaction:** label completion selects; existing action keys act. No automatic left-click on label completion.

## Approved implementation choices

The user approved the complete plan on 2026-10-07: "good, commit, then implement on new gpt 6.1 sol high default subsession". The choices below originated as planning proposals and are accepted through that approval; they are not presented as individually quoted user selections.

| Choice | Decision | Rationale |
|---|---|---|
| Mode name | `ElementHints` / `modes.elementHints` | Distinguishes semantic targets from coordinate grids. |
| Rollout | Disabled by default; existing default mode unchanged | Introduces no key capture/startup helpers until explicitly enabled. |
| Chord | Tab, configurable and fully conflict-validated | Outside the shipped axis/action/mode/help/macro bindings. A user collision must be surfaced, never rebound. |
| Grid fallback | Enter from any element-mode state; UniformGrid required when enabled | No new global hotkey; reachable even when normal chord switching is locked. Retains coordinate coverage for missing UIA controls. |
| Labels | Existing horizontal-first/vertical-second Cartesian pairs per page | Uses current layout-aware keys and familiar two-key selection. No arbitrary Latin-only alphabet or unbounded label length. |
| Paging | Left/Right previous/next, with page/count indicator | Uses already-reserved keys without colliding with PageUp/PageDown scroll hotkeys. Requires the mode's arrowKeys flag; Up/Down are not directional target navigation in this version. |
| Navigation flags | Enabled ElementHints requires `twoKey=true`, `arrowKeys=true` | Keeps every retained target reachable through selection and paging; do not silently accept settings the mode ignores. |
| Escape | Clear prefix/selection first; cancel from neutral/loading/error | Matches staged navigation, with immediate cancellation available during discovery. |
| UIA API | Managed `System.Windows.Automation` on .NET 10 Windows first | No third-party dependency expected; verify framework references before proceeding. |
| Discovery deadline | 1500 ms, including helper startup | Bounds app-visible wait. Responsive Escape/Enter is separate from provider completion. |
| Validation deadline | 500 ms | Bounds action delay; rejection rather than stale cached-coordinate click. |
| Process cleanup deadline | 500 ms after termination request | No unbounded UI-thread exit waits; failure is explicit and prevents replacement-worker accumulation. |
| Discovery caps | 20000 visited nodes, depth 64, 2000 retained targets | Bounded provider data and rendering. Exceeding a cap produces explicit partial status, not complete success. |
| Wire caps | Request 64 KiB; response 2 MiB; diagnostic stderr 64 KiB; runtime ID at most 64 integers | Bound reads/allocation and malformed output. Change protocol caps together with tests if real fixtures demonstrate a necessary increase. |
| Responsiveness acceptance | Controlled-fixture Escape/Enter visibly handled within 100 ms while child is blocked, excluding unrelated OS scheduling stalls | Measures navigation responsiveness independently of scan deadlines; hermetic tests assert cancellation/fallback does not await provider work. |
| Action semantics | Physical input through existing dispatcher/mouse service only | Preserves right/double/modifier clicks, move-only, drag, and coordinate macro semantics. |
| Stale geometry | Reject moved/replaced targets; reopen for fresh labels | Stable snapshots rather than automatic retargeting under an old label. |
| Helper lifetime | One process per active snapshot, retired on cancellation/replacement/suspension; kill-on-close job | Enables fresh validation against retained identity and bounded recovery, including parent exit. |
| IPC/build | Inherited redirected stdio; small shared protocol source; isolated bundled worker subfolder | No publicly addressable endpoint, app-assembly dependency, shell launch, or additional contracts framework. |
| Privacy | No element text values, names, passwords, or document text in hints/IPC/logs | Geometry/capability/identity is sufficient for this feature. Log counts, durations, status/error codes, not tree content. |
| Refresh | New snapshot on activation/resume; no event-driven continuous relabeling | Keeps the first version bounded and avoids selection moving under the user's keys. |
| Configuration | Version 9, additive disabled-mode migration | Current code uses version 8; preserve unknown fields and configured bindings. |

## Resolve-or-stop gates

- Whole-plan intent, requirements, risks, defaults, and numeric thresholds were approved on 2026-10-07. Persist the existing planning-confirmed marker through the repository stage tool before execution.
- If actual SDK references require a dependency or native-interop redesign, stop for that specific choice; no guessed NuGet package.
- If the worker cannot be bounded, or extracted publishing requires manual deployment/system modifications, stop rather than weaken isolation/distribution requirements.
- If an app exposes no verifiable target identity/point, display the limitation and use grid; do not broaden to screenshot/remote/app-specific detection.
- Live third-party results determine documented compatibility, not a claimed guaranteed supported-app list.

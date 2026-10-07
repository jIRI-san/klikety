# Implementation evidence

## Environment and base

Recorded 2026-10-07 in the isolated `jiri-san-uia-element-hints` worktree, using
the default agent. Approved plan commit
`c3759d7fd69723f33263c293831541f263d66e9c` is an ancestor of the implementation
base. Approved intent, requirements, risks and decisions are unchanged.

Windows 11 Pro, version 10.0.26300, build 26300; .NET SDK 10.0.401; test runtime
.NET 10.0.12. No new third-party dependency, elevation, UIAccess, browser flags,
remote detection or system-wide SDK change.

## Delivered code

| Steps | Delivered implementation | Observed verification |
|---|---|---|
| 1.1 | Windowless MTA managed UIA worker; captured HWND/PID/start identity; bounded versioned stdio; job ownership; serialized retirement/replacement; startup-inclusive scan and separate validation deadlines. | Development worker runs. Hang, crash, truncated/malformed/oversized response, diagnostic overflow, missing executable, cancellation, same-supervisor recovery, validation timeout and parent-crash cleanup fixtures pass. No worktree-owned helper/fixture remains after checks. |
| 1.2 | Opt-in fifth mode, Tab chord, additive version 9 migration, partial-settings defaults, fail-closed conflict policy, explicit target context, two-key selection and locked/loading/error Enter fallback. | State, configuration, non-QWERTY/default activation, selection-without-click and locked fallback tests pass; existing config/mode regressions pass. |
| 2.1 | Control-type/pattern classification, readonly edits, disabled/offscreen/invalid geometry exclusion, runtime-ID dedup, nested-action retention, clipped physical geometry and bounded incremental control-view traversal. Worst-case response bytes trim to an explicit partial snapshot. | Policy, geometry, 64-bit HWND serialization and worst-case runtime-ID/response-byte tests pass. Real WPF fixture discovery finds independent button, two edits and toggle while excluding passive text/disabled controls. Dense-provider node/depth/target-cap traversal and every independent nested-action/cross-process branch case are not exhaustively exercised. |
| 2.2 | Deterministic horizontal/vertical labels, stable pages, prefix filtering, measured outlined WPF labels, inline/list layout, connectors and contained extreme-case scrolling. | Every fake retained target is selectable across pages without clicking. Actual STA WPF geometry tests cover crowded controls, custom/long fallback labels, negative origins and simulated 100/150/200% coordinate scales. This is not live multi-monitor/DPI evidence. |
| 3.1 | Captured action/modifiers; capture drain and whole host/HUD hide; fresh UIA/native identity, state, ancestry and point checks; final native ownership check; exactly-once existing physical dispatcher; cancellation fences. | Fake-service/guard tests prove hide-before-validation, released modifiers, action variants, rejection, duplicate suppression, cancel/reopen/late completion, recording rejection and drag reset. Final real WPF tests pass healthy validation and rejection after move/disable/replacement. Covered-center, broader nested-action and physical delivery verification remain pending. |
| 3.2 | Explicit target through scope/display/drag/recording/playback paths; fresh picker/playback resume; independent status/help layers; cleanup failure reporting retained until retirement completes. | Element-specific scope/display/picker/recording/playback-resume target/region tests, help/layout/default activation, cleanup failure reporting and existing display/scope/macro regressions pass. Full live focus/config/topology/HUD and macro-input combinations remain unobserved. |
| 4.1 | Isolated worker build/publish folder, extracted release ZIP handshake gate, README and related design notes. Test-only fixture is not shipped. | Final self-contained win-x64 publish, extracted-ZIP handshake, matching app/worker version and RID, bundled UIA/core runtime files and fixture exclusion pass. |

## Final successful commands

Run from the implementation worktree:

```powershell
dotnet build Klikety.slnx -c Release --no-restore --verbosity quiet
dotnet test src\Klikety.Tests\Klikety.Tests.csproj -c Release --no-build --no-restore --verbosity quiet
dotnet format Klikety.slnx --no-restore --verify-no-changes --verbosity quiet
dotnet publish src\Klikety\Klikety.csproj -r win-x64 --self-contained true -c Release -p:Version=1.1.0 -o src\Klikety\bin\uia-final-publish --verbosity quiet
Compress-Archive -Path src\Klikety\bin\uia-final-publish\* -DestinationPath src\Klikety\bin\uia-final-release.zip
Expand-Archive -LiteralPath src\Klikety\bin\uia-final-release.zip -DestinationPath src\Klikety\bin\uia-final-extracted
.\scripts\Test-UiaWorkerPackage.ps1 -PublishDirectory src\Klikety\bin\uia-final-extracted
git diff --check
```

Release solution build: **0 warnings, 0 errors**. Hermetic suite: **959 passed,
0 failed, 0 skipped**. Formatting and whitespace checks pass.

The package script verifies a self-contained runtime manifest, app/worker version,
required worker/UIA/core runtime files, bounded protocol Hello with matching
version/session/request IDs and bounded normal exit. The child uses empty PATH,
invalid DOTNET_ROOT/DOTNET_ROOT_X64 and disabled multilevel runtime lookup.
Separate extracted-artifact checks confirm matching
`.NETCoreApp,Version=v10.0/win-x64` runtime targets and no WorkerFixture files.
The machine still has an SDK installed; no claim of testing a physically SDK-free
machine is made.

One package-gate invocation immediately after fresh extraction failed with
`Worker handshake timed out`. An unchanged rerun against the same extracted
artifact passed, as did earlier extracted-artifact checks. Cause was not
established; no deadline was increased. Cold-start timing/reliability still needs
measurement on the live test desktop.

## Live gate: partial evidence, not waived

```powershell
dotnet test src\Klikety.SmokeTests\Klikety.SmokeTests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ElementHintSmokeTests --verbosity minimal
```

Final Release run: **3 passed, 0 failed, 0 skipped**. All three controlled WPF
cases (`move`, `disable`, `replace`) pass discovery assertions, healthy validation
and fail-closed validation after mutation. No physical mouse input is injected by
these tests.

Earlier runs: **0 passed, 3 failed, 0 skipped**. Discovery assertions passed, then
healthy validation failed before mutation at:

```text
StaleTarget: Application lost foreground
```

Ordinary `ShowActivated=true` and foreground-restoration attempts initially did
not yield usable fixture foreground ownership. The final Release run did; cause
of the intermittent foreground failures is unknown. The healthy validation
assertion and foreground guard remain intact, and deadlines were not weakened.
Do not convert foreground failure into passing stale-mutation evidence.

Remaining acceptance: use an interactive Windows desktop that can activate the
fixture, then complete covered/nested-point and physical
click/modifier/move/drag verification, full lifecycle/focus/macro scenarios, actual
negative-origin/mixed-DPI displays, representative WinUI/browser/Electron/custom
providers and elevated-app denial. Record app/OS versions and observed coverage.
Measure Escape/Enter at the approved <=100 ms threshold while the helper is
blocked; hermetic non-awaiting behavior is not a substitute for that timing.
Also finish dense-provider traversal-cap and remaining nested/provider fixture
coverage before checking the later plan contracts.

Steps 2.1-4.2 remain open for their complete acceptance evidence. The plan remains
`drafted` with its existing approval; whole-plan completion and universal provider
coverage are not claimed. No push, merge or pull request is authorized.

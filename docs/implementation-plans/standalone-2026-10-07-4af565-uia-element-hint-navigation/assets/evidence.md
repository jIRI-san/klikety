# Implementation evidence

## Environment, base and persisted implementation

Recorded 2026-10-07 in the isolated `jiri-san-uia-element-hints` worktree, using
the default agent. Approved plan commit
`c3759d7fd69723f33263c293831541f263d66e9c` remains an ancestor of HEAD.
Initial implementation commit `f1b2d42aaa447b05a0f724c525408ac2b7e0ea1e`
is preserved; this acceptance follow-up is a separate commit. Approved intent,
requirements, risks and decisions are unchanged.

Windows 11 Pro 10.0.26300/build 26300; .NET SDK 10.0.401; test runtime .NET 10.0.12.
No new third-party dependency, elevation, UIAccess, browser flags, remote processing
or system-wide SDK change. All discovery/validation/cleanup limits remain unchanged.

## Delivered and automated contracts

Checked steps mean implementation plus the automated contracts below, not the
unperformed live scenarios explicitly assigned to human step 4.2.

| Step | Delivered implementation and observed evidence |
|---|---|
| 1.1 | Windowless MTA worker; captured HWND/PID/start identity; bounded versioned stdio; kill-on-close job; serialized retirement/replacement. Development protocol and hang/startup-hang/crash/truncated/malformed/oversized/stderr-overflow/missing-executable/cancellation/recovery/validation-timeout/parent-crash-cleanup tests pass. Startup-hang occurs before reading the request, exercising the startup-inclusive 1500 ms budget. |
| 1.2 | Opt-in fifth mode, Tab chord, additive version 9 migration, partial-setting defaults, explicit target context, selection without clicking, locked/loading/failure Enter fallback. Config, non-QWERTY/default activation and existing mode/config regressions pass. |
| 2.1 | Native adapter uses the production `UiaTreeAlgorithms` kernel linked into hermetic tests. Exact 20000-node, depth-64 and 2000-target caps; nested independent actions/passive text; runtime-ID dedup without rectangle dedup; legitimate cross-process descendants; own-process exclusion; readonly edits; all five custom pattern capabilities; clipping; malformed PID/runtime ID/geometry; branch failures and root/unexpected error propagation pass. Invalid interactive identity/geometry now reports partial omissions; invalid root geometry rejects discovery. Protocol byte-budget and 64-bit HWND tests pass. |
| 2.2 | Every retained fake target is reachable through stable Cartesian labels and paging, with no click on selection/page changes. Actual STA WPF tests cover crowded/custom/long labels, negative origins and simulated 100/150/200% coordinate scales. New 7x20, 1x1 and 100x60-DIP tests prove status/list scroll regions remain contained without reducing the font floor or dropping labels. Longer-glyph redraw preserves page/prefix/selection/assignments using scrolling; explicit relayout alone recomputes pages. |
| 3.1 | Captured action/modifiers, capture drain, whole host/HUD hide, fresh root/target/ancestry checks, native/UIA point ownership, final native guard and exactly-once dispatcher remain intact. Production-kernel tests cover passive text hits versus independent actions, covered center/quarter-point search, total coverage, out-of-area preferred points, foreign native windows, PID/start/runtime-ID root reuse, target state/identity/geometry changes and cyclic/deep ancestry. Coordinator tests cover action variants, released modifiers, timeout/rejection, duplicates, late/cancel/reopen results, rejected recording and drag reset. Real-provider destruction and observed foreground-loss rejection pass; physical delivery is not observed. |
| 3.2 | Explicit original target/region survives scope/display/picker/recording/playback resumes. Tests cover topology events, focus loss and coordinator config-disposal while validation is pending, disposal during discovery, cleanup reporting, help preserving both prefix/selection on a later page, layout refresh without rescanning, default activation and locked fallback. Existing display/scope/help/macro/drag regressions pass. Live mixed-display/HUD/macro-input combinations remain in 4.2. |
| 4.1 | Release build, formatting, 987 hermetic tests and final fresh self-contained win-x64 publish pass. Extracted ZIP passes versioned handshake under empty PATH/unavailable DOTNET_ROOT, matching app/worker version and RID, runtime/UIA files and test-fixture exclusion. README and relevant design notes describe actual behavior and limitations. |

### Defects corrected in this follow-up

- Fixed fixed-offset rendering outside tiny viewports: shared adaptive status/list
  regions are used for capacity and painting, retaining initial fallback dimensions.
- Invalid interactive identity/geometry no longer disappears without partial
  diagnostics. Invalid root geometry is rejected.
- Native PID/process-start guards precede UIA reacquisition of a possibly reused
  HWND; current foreground is sampled after fresh root metadata.
- Supervisor checks cancellation/deadline after response decoding and validation,
  not only during pipe I/O.
- Package gate timing includes process creation and response validation, with
  separate frame-receipt and PowerShell parsing measurements.
- Development packaging assertions now report outcome/reason/elapsed time/owned PID,
  and retire their helper in `finally`, including on assertion failure.

## Final build, tests and distribution

Run from the implementation worktree:

```powershell
dotnet build Klikety.slnx -c Release --no-restore --verbosity quiet
dotnet test src\Klikety.Tests\Klikety.Tests.csproj -c Release --no-build --no-restore --verbosity quiet --results-directory src\Klikety.Tests\bin\uia-followup-results --logger "trx;LogFileName=final.trx"
dotnet format Klikety.slnx --no-restore --verify-no-changes --verbosity quiet
dotnet publish src\Klikety\Klikety.csproj -r win-x64 --self-contained true -c Release -p:Version=1.1.0 -o src\Klikety\bin\uia-followup-publish --verbosity quiet
Compress-Archive -Path src\Klikety\bin\uia-followup-publish\* -DestinationPath src\Klikety\bin\uia-followup-release.zip
Expand-Archive -LiteralPath src\Klikety\bin\uia-followup-release.zip -DestinationPath src\Klikety\bin\uia-followup-extracted
.\scripts\Test-UiaWorkerPackage.ps1 -PublishDirectory src\Klikety\bin\uia-followup-extracted -Diagnostics
git diff --check
```

Final Release build: **0 warnings, 0 errors**. Full hermetic suite: **987 passed,
0 failed, 0 skipped**. Formatting passes. Local TRX evidence is in the ignored
test output directory. Published production source is unchanged by the final
test-only help/diagnostic additions.

Final extracted package: preparation **42 ms**, process start **77 ms**, write
**97 ms**, frame received **878 ms**, parsed **887 ms**, worker readiness
**642 ms**. App/worker runtime targets both match
`.NETCoreApp,Version=v10.0/win-x64`; WorkerFixture is absent. The gate also checks
matching versions, required bundled runtime/UIA files, protocol IDs/outcome and
bounded normal exit. Remaining worktree-owned worker/fixture processes: **0**.
The host has an installed SDK; this is bundled-runtime isolation evidence, not
a physically SDK-free machine test.

## Bounded startup investigation

The baseline extracted-package failure was `Worker handshake timed out`; an
unchanged retry passed. Its underlying cause is still not established.

Five diagnostic runs against the same follow-up publish folder all passed.
The initial instrumented gate measured response time together with later
PowerShell parsing, and started its clock after `Process.Start`:

| Run | Reported response including parsing (ms) | Worker readiness (ms) |
|---|---:|---:|
| 1 | 1549 | 1078 |
| 2 | 663 | 302 |
| 3 | 446 | 175 |
| 4 | 346 | 150 |
| 5 | 320 | 148 |

Run 1 also recorded process start **115 ms**, serialization **173 ms** and write
**203 ms** under that earlier instrumentation. These are not startup-inclusive
acceptance timings. They expose the harness measurement flaw, not proof of the
historical timeout's cause. The longer readiness localizes delay before the worker
is ready; it does not identify the OS/runtime reason.

Corrected gate: prepare JSON before process creation, start the absolute 1500 ms
clock before `Process.Start`, measure frame reception separately, and reject if
decoding/validation finishes after the deadline. Three fresh diagnostic ZIP
extractions passed:

| Fresh extraction | Start / write / received / parsed (ms) | Readiness (ms) |
|---|---|---:|
| 1 | 49 / 59 / 674 / 681 | 495 |
| 2 | 42 / 42 / 659 / 659 | 466 |
| 3 | 41 / 41 / 630 / 630 | 454 |

The final-source fresh extraction above is the fourth corrected-gate pass.
No deadline was raised. The original handshake timeout was not reproduced.

Additional follow-up uncertainty: one initial full-suite run reported **985
passed, 1 failed** at `BundledDevelopmentWorkerRunsVersionedProtocol`; the quiet
runner did not retain its assertion detail. The focused test passed in **362 ms**,
and controlled warm full runs subsequently passed (986 before the final help
matrix; 987 finally). Later runs do not establish the original failure cause.
Future packaging failures now include typed outcome/timing diagnostics and
guaranteed fixture cleanup.

## Controlled real-provider/focus investigation

Fixture readiness now waits for `ContentRendered`, after its dispatcher is
running. It logs WPF activation and native foreground HWND. No forced input,
`AttachThreadInput`, permission change or foreground-guard bypass is used.

Two distinct bounded matrices:

1. Fixture-side ordinary `Activate`/`SetForegroundWindow`: **8/8 cases failed
   before discovery** because native foreground acquisition was denied.
2. Add ordinary parent-side `SetForegroundWindow` and a bounded 200 ms settle:
   **8/8 cases again failed before discovery**. Parent acceptance was `False`;
   a different native HWND remained foreground.

WPF `IsActive=True` did not prove native ownership. These results identify denied
activation in the current fixture execution context, not a timing assumption or
permission to treat stale validation as healthy. Healthy/mutation/covered-point
assertions remain intact and failing when acquisition is denied.

Independent tests that do not require acquiring foreground:

```powershell
dotnet test src\Klikety.SmokeTests\Klikety.SmokeTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DestroyedCapturedWindowRejectsBeforeForegroundOrPointValidation|FullyQualifiedName~RealWpfDiscoveryStaysWithinRootEvenWhenAnotherWindowIsForeground" --verbosity quiet --logger "console;verbosity=detailed"
```

Initial root-only run passed: real WPF discovery **Success**, **10 visited nodes,
4 targets** (button, two edits, toggle; no passive text/other root).
Validation rejected in **6 ms** with
`Application lost foreground (expected=21303354, actual=984816)`; native
foreground was `984816` both before and after. This is directly observed
fail-closed behavior for actual foreground loss.

A later build-adjacent combined run had **1 pass/1 failure**: destroyed-window
rejection passed, root-only discovery returned **Timeout**, with zero
visited/targets. A separate warm no-build run passed **2/2**:

- Root-only discovery: **Success**, **10 visited, 4 targets**. Validation:
  `StaleTarget: Application lost foreground (expected=30281946, actual=85331832)`,
  **11 ms**; foreground `85331832` before and after, no approved point.
- Destroyed captured window: `StaleTarget: Application or snapshot changed`,
  **4 ms**, no approved point.

The cold discovery timeout's phase/cause is not established; an unchanged warm
success is not a fix. Bounded attempts stop here rather than weakening budgets or
looping. No physical input was injected.

Historical baseline, not the latest expanded matrix: 959 hermetic tests and a
final **3/3** WPF healthy/move/disable/replacement run passed. Earlier baseline
healthy validations failed with `StaleTarget: Application lost foreground`.
The new activation/foreground evidence does not retroactively prove the cause of
every historical failure.

## Remaining human gate and unresolved conditions

Step **4.2 remains unchecked**, stage **drafted**, existing approval preserved.
Whole-plan completion and universal provider coverage are not claimed.

Use an interactive desktop that permits normal fixture activation to observe
healthy/moved/covered/nested real-provider validation and physical
click/modifier/move/drag delivery. Complete live focus/help/recording/picker/playback,
negative-origin/mixed-DPI displays, representative WinUI/browser/Electron/custom
providers and normal-user/elevated-app denial, recording versions and omissions.
Measure visible Escape/Enter handling against the approved **<=100 ms** threshold
while the worker is blocked; hermetic non-awaiting behavior is not timing evidence.

Unresolved: denied fixture foreground acquisition, the historical fresh-package
handshake timeout, the cold root-only discovery timeout and the initial
development-package test failure. They are documented, not attributed to a
runtime defect or declared fixed by retry. No push, merge or PR is authorized.

## Sandbox visual-clutter follow-up (2026-10-08)

User feedback described a black diagnostic bar, top-clustered pairs and unreadable
connectors. Windows Sandbox was already enabled (`Containers-DisposableClientVM`
InstallState 1; hypervisor present). No features, elevation or security settings
were changed. The owned modern Sandbox guest used Windows 26100 and
WDAGUtilityAccount, with networking, clipboard, audio/video and printer redirection
disabled. Only the scratch package/config/harness directory was mapped read-only;
a separate narrow capture directory was writable. No host worktree, AppData,
private files or user applications were exposed to guest discovery/input.

The guest harness created a harmless WinForms fixture and used guest-only Alt,
activation chord, Tab, A/Q and Escape. No action/click keys were sent. Package
execution was normal-user, copied from the read-only mapping to `C:\GuestApp`.
The native screen was **3046x1650 physical pixels**, overlay **1523x825 DIPs**,
**200% scale**. Before and successful initial-after fixture-geometry files have the
same SHA256 `0F7A32EAF0E1EE40E9B7D78BBC3F87CF3AE64106EBEF5FC0593891C6C20A833C`.

| Actual capture | Observation |
|---|---|
| Before, successful baseline | Partial snapshot: 48 controls, 5 invalid-geometry omissions. A top target/collision switched the entire page to two global top rows with all-target connectors. Black diagnostic banner began at (16,16), was 3014 physical pixels wide and reserved 128 pixels vertically. |
| Initial rendering fix, successful guest run at 07:10 | Partial: 48 retained, 57 visited, 5 omitted; same fixture geometry. All labels stayed near their targets, no unselected connectors or outlines. Prefix capture highlighted its target group; AQ selection highlighted the target and dimmed others. Footer black-pixel bounding box: **(923,1579), 1200x55 physical pixels**. Counts/reasons were in debug logs, not the footer. |
| Final package, checked-ready run at 07:19/07:20 | Native owned HWND/PID and occupied activation hotkey verified; overlay became foreground under app PID 14688 and was responsive. Discovery returned **Timeout, 0 visited/retained**. Actual loading/timeout captures show concise footer/fallback, but are not successful label-layout evidence. |
| One bounded warm final-package attempt at 07:21 | Ownership/readiness verified under PID 4108; again **Timeout, 0 visited/retained**. No further retries or raised budgets. Cause is unresolved. |

The root rendering defect was deterministic: any top-edge/overlapping label
triggered whole-page fallback, while fallback painted a connector for every
target and a full-width technical status banner. The fix uses bounded nearby
placement with gaps, selected-only displacement connectors/outlines and a compact
bottom footer. Severe crowding retains a right-side page list, original VKeys and
physical target coordinates. Font size/floor is unchanged; the user's permitted
1-2-point reduction was not needed for the successful capture. Final capacity now
also fits the narrower fallback list. Frozen longer-glyph redraws scroll to the
selection/prefix and keep the connector aligned with the actual scroll offset.
These later list/paging/scroll details have deterministic WPF coverage; they were
not demonstrated by the final guest attempts that timed out.

Harness limitations and bounded corrections were recorded rather than hidden:

- Windows PowerShell 5.1 did not recognize `[ushort]`; harness uses `[UInt16]`.
- Native foreground acquisition initially failed. A guest-only Alt event before
  normal SetForegroundWindow permitted the harmless fixture to activate. This is
  test harness input, not a production foreground-guard change.
- Two read-only mapped-runtime attempts timed out; copying runtime files to
  guest-local storage then yielded the successful before/initial-after captures.
  This does not establish every historical timeout's cause.
- One final-package attempt captured only the fixture, not an overlay. It is
  excluded from successful evidence. A title-only window lookup was invalid, and
  an owned native window at 952 ms did not imply hotkey registration: the chord
  was still free. A bounded 8-second harness startup settle plus native
  ownership/registration and post-activation checks prevented fixture-only
  captures from being counted. This startup settle is outside, and does not
  modify, the worker's 1500 ms discovery budget. It does not retrospectively
  prove the exact cause of the earlier missed activation.

Local ignored evidence is under
`src\Klikety\bin\uia-sandbox-20261008`: `baseline-evidence` contains actual before
PNG/JPEG captures, fixture geometry and logs; `before-output\after` contains the
successful initial-fix captures. `before-output\final-ready` and `final-warm`
retain the final timeout evidence; `final-after` is fixture-only and
`final-diagnostic`/`final-owned-window` contain harness diagnostic failures.
`input` retains the minimal config and guest-only reproduction/inspection tools.

Final source verification: Release solution build **0 warnings/0 errors**,
**990 hermetic tests passed**, including **11 actual STA rendering cases**,
formatting passes. New cases cover ordinary top-edge/collision placement, no
connector soup, bounded crowding and fallback-list capacity. Existing
font-floor/negative-origin/DPI/tiny-viewport/frozen-page regressions still pass;
the frozen-page test additionally observes scrolling and connector alignment.
Fresh extracted self-contained package gate passed with PATH empty and
DOTNET_ROOT unavailable: start/write/received/parsed **42/54/619/625 ms**,
worker readiness **448 ms**. Source/publish paths for this follow-up use
`uia-visual-publish`, `uia-visual-release.zip` and `uia-visual-extracted`.

The guest app/helpers were absent after exact-path inspection; the owned Sandbox
guest `b22363fb-3045-4c15-9c5a-8dd944d5cc8a` was stopped and guest listing was empty.
Host PID **6508**, its previous `uia-followup-extracted\Klikety.exe`, and user config
were left untouched. Config SHA256 remained
`AB81C7C0C4BEB5478CFC2C1C6089D0769FFA473124682CC00C04859534BE27EF`.
The running host app is therefore still the earlier build, not silently replaced.
Human **4.2 remains open**. Final guest discovery timeouts, earlier startup/focus
uncertainties and unobserved physical/mixed-display/third-party scenarios remain
explicitly unresolved; successful initial-fix captures are not universal or
whole-plan acceptance.

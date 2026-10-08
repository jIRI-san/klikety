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

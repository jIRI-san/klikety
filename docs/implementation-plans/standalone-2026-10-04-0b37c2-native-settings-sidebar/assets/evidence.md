# Settings implementation evidence

## User-directed verification correction (2026-10-08)

The user's exact decisions are retained in [intent](intent.md):
"skip these, i will validate and fix issues when needed. just finish the plan and merge",
then "Defer those checks to me; finalize, archive, and merge".
The affected CIP criteria now distinguish nondeferred completion from user-owned
native interaction/DPI follow-up. These decisions supersede the prior publication
hold without inventing missing evidence or changing production behavior.

| User-owned remaining manual row | Evidence state |
|---|---|
| Actual 100/150/200% all-page layout, >=1280x720 DIP | Only actual 96 DPI/work-area measured; complete matrix not exercised |
| Actual scrolling effect, pause/resume preservation and tray agreement | Real up/down registrations and saved amount observed; wheel effect not established |
| Macro activation/picker/playback/indicator and live recording/playback busy rejection | Managed contracts pass; actual interaction not exercised |
| Enabled HUD refresh/failure, live hook/window count and tray agreement | Startup-disabled visual save observed; active native row not exercised |
| Fixture tray conflict reservation/release and live input-dependent controls | Managed owner-aware contracts pass; tray interaction not exercised |
| All-page keyboard-only edits/lists/capture/Advanced/error/Save/Discard/Close and announcements | General keyboard/cancel/confirmation observations retained; complete live workflow not exercised |

No unsupported row is recorded as passed. The user's correction makes these
follow-up rows nonblocking for plan finalization; managed failures, unsafe isolation
or a demonstrated code defect would still stop publication.

## Isolated Sandbox progress and eligible step closures

The approved, already enabled Sandbox started successfully using installed
`MicrosoftWindows.WindowsSandbox` version `0.8.107.0`. Its environment ID is
`a1da0308-b75c-4660-b59d-3287df458ff5`, remote-session PID 40304. The guest user
is `WDAGUtilityAccount`; the actual Release runtime fixture PID is 16740 and
all mutable app files are confined to `C:\KliketyFixture`. Only the self-contained
app and runner folders were mapped read-only, with a dedicated writable evidence
folder. Networking and clipboard sharing are disabled. Host production PID 37704
remained responsive; no host config, registry, DPI or process was changed.

Native observations, screenshots and transcripts are retained locally under
`C:\Users\jiri\.copilot\session-state\b833fd48-09d8-47d2-9bda-332fde5a7f44\files\settings-sandbox\results`.
The bundle is built from the production source of `58f1500`; later `f843f24`
only commits validation/review documentation.

| Observed native check | Result |
|---|---|
| Actual fixture tray Settings entry opened twice | Reused HWND 131676 |
| General Save/apply, Close, tray reopen | Saved retained log count matched fixture disk; real main shortcut remained registered |
| General keyboard editing, Alt+C/No, Alt+D/Yes | Close-cancel retained draft; Discard cleared it without changing disk |
| Focused key capture Escape | Cancelled without changing the clean draft |
| Navigation edit | Uniform-grid size saved through actual captured runtime composition |
| v8 help fields | Require Shift edited/saved through the actual editor |
| Appearance/logging | Light theme applied; real log file created only inside fixture root |
| Scroll enabled/amount | Saved; actual Ctrl+Alt+PageUp/PageDown registrations observed |
| Candidate main fault and retry | Explicit failure, retained candidate, exact previous config/backup SHA256, real old main registration restored; retry succeeded |
| Candidate logger/overlay/coordinator/main/scroll-up/scroll-down/scroll/macro/indicator faults | Each executed in the actual runtime, reported its exact injected stage, retained draft, restored previous disk bytes/protected original backup and restored real main registration |
| Candidate disk-save fault | Explicit refusal before disk change; retained draft and real main registration |
| Disabled-HUD visual edit | Saved through actual runtime; enabled-HUD refresh is not established |
| External edit during candidate activation | Newer candidate bytes/comment and original backup retained; previous native main restored; Save blocked until explicit reload; edited Save resynchronized |
| Independent disk-restoration failure | Saved candidate disk retained, original backup protected and prior runtime restored; explicit reload/edited Save resynchronized |
| Independent runtime-restoration failure | Exact old disk restored, backup protected, candidate retained, explicit runtime error and missing main registration observed; explicit Save retry rebuilt native runtime successfully |

The runtime-recovery runner initially assumed every runtime restoration failure
must disable Save. `SettingsSaveTransaction` requires reload only when disk restoration
fails; stable restored disk deliberately permits an explicit retained-draft retry.
The unsupported runner assumption was corrected against that existing contract,
not by changing production code or criteria. Its subsequent complete run returned
guest exit 0 and observed the real main registration restored on retry.
The file-recovery assertions succeeded, but their final screenshot returned
`The handle is invalid` and the script exited 1; that screenshot is not credited.

Only checks actually observed above are credited. The scripted scroll target's
visible line remained `0 -> 0`; registration succeeds but a real wheel effect is
**unverified**, not passed. No send-scroll error was found in the fixture log.
Later guest `SendKeys` returned exact `Access is denied`. Checked Win32 `SendInput`
returned **0 of 8 inputs, error 5**; guest session 1 was active on input desktop
`Default`. No locking/minimization cause is inferred and no privilege bypass was
attempted. This is an input-runner
limitation, not evidence of a production app defect; physical activation, pause,
recording/playback and enabled HUD still need an interactive native operator.
The accessible native Yes/No confirmations are invoked only after verifying their
button class and ownership by the exact guest fixture PID.

The final bounded macro-options/native conflict attempt did **not execute**:
`The Windows Sandbox ID provided was not found.` The installed CLI subsequently
reported `WindowsSandboxEnvironments: []`; remote-session PID 40304 was absent.
The previously successful native assertions remain evidence. No unexecuted script
is credited; no Sandbox was relaunched and no host process was stopped.

`GetDpiForWindow` on Settings returned **96 DPI / 100%**, with actual work area
**3056x1639 DIP**. This identifies the running display; it does not pass the
all-page keyboard/layout row. The guest's System Settings has no Display page:
`ms-settings:display` opened Home, and explicit System navigation still exposed
no Display entry. Installed `wsb connect --help` has no scaling option. Neither
150% nor 200% was exercised. No host scaling, guest registry hack, app transform
or process-DPI override was used as matrix evidence.

Direct class evidence was extracted from the already-green full-suite TRX and
passed `Invoke-DirectEvidence`:

| Class | Passed |
|---|---:|
| SettingsDraftTests | 3 |
| SettingsWindowTests | 10 |
| SettingsApplyTests | 31 |
| SettingsRecoveryTests | 10 |
| SettingsConfigStoreTests | 36 |
| SettingsKeyEditorTests | 12 |
| SettingsRoundTripTests | 5 |
| SettingsNavigationTests | 4 |
| SettingsValidationTests | 54 |
| SettingsCoverageTests | 1 |
| SettingsIsolationTests | 14 |
| SettingsThemeTests | 1 |

The original `ThemeLoaderTests` marker lookup had no current class and was
rejected, not treated as a suite failure or passing evidence. Current theme
contracts are exercised by the named Settings isolation/validation/theme cases.
The complete ordinary managed run remains 1304 passed, 0 failed, 0 skipped.

Closure proceeded in prerequisite order: **1.2** combines draft/window tests with
actual tray reuse and fixture General keyboard/confirmation checks; **2.2**
combines file/apply/recovery tests with actual General save/reopen and native
failed-apply/retained-draft/retry observations. Their closure admits **3.1-3.3**:
collection/picker/capture, navigation/scope and appearance/diagnostics contracts
are verified by the named store/editor/round-trip/validation/isolation cases,
plus the native observations above. The all-page live usability matrix is still
owned by 5.2/5.3; those criteria were not folded into these closures.

| Step | Current state | Remaining requirement |
|---|---|---|
| 1.1, 1.2, 2.1, 2.2, 3.1, 3.2, 3.3 | Complete | Direct evidence above; no independent earlier-step blocker identified |
| 4.1 | Pending | Real scroll effect, pause/resume preservation and tray state |
| 4.2 | Pending | Interactive macro activation/indicator and recording/playback busy observations |
| 4.3 | Pending | Enabled HUD refresh, live hook/window behavior and tray-state agreement |
| 5.1 | Dependency-blocked | Its machine coverage/isolation/fault tests pass; 4.1-4.3 remain open |
| 5.2 | Dependency/native-blocked | 5.1, every-page keyboard/announcements and genuine 100/150/200% matrix |
| 5.3 | Human/native-blocked | 5.1/5.2 and complete actual isolated runtime/display exercise |
| 6.1 | Dependency-blocked | 5.3, final reconciliation, terminal review, compaction and learning handoff |

**Seven of fourteen steps are closed.** This is not finalization. The earlier
archive refusal at 2/14 is historical; no subsequent archive or publication
attempt was made. User-directed merge hold remains until native evidence and
successful whole-plan finalization permit scripted archival.

## Final integrated managed validation and publication hold

Source: `58f15006bfd7d23f427b4761b33e992b730926d6`, containing the v8/main
integration and guarded Close action. Fetched `origin/main`
`e1073ea31e9778a8b9e04e38cfdc301f645791cb` is already an ancestor; integration
reported `Already up to date`.

The complete ordinary managed suite passed **1304 passed, 0 failed, 0 skipped**.
This was one unfiltered full run, not selected reruns of earlier timing failures.
The Release production build passed with **0 warnings, 0 errors**. Owned alternate
outputs avoided touching the user's running production executable:

```powershell
dotnet test .\src\Klikety.Tests\Klikety.Tests.csproj -c Release --no-restore `
  '-p:BaseOutputPath=C:\Users\jiri\.copilot\session-state\b833fd48-09d8-47d2-9bda-332fde5a7f44\files\settings-final-validation\bin\' `
  --verbosity minimal --logger 'console;verbosity=minimal' `
  --logger 'trx;LogFileName=settings-full-suite.trx' `
  --results-directory 'C:\Users\jiri\.copilot\session-state\b833fd48-09d8-47d2-9bda-332fde5a7f44\files\settings-final-validation\results'

dotnet build .\src\Klikety\Klikety.csproj -c Release --no-restore `
  '-p:BaseOutputPath=C:\Users\jiri\.copilot\session-state\b833fd48-09d8-47d2-9bda-332fde5a7f44\files\settings-final-validation\bin\' `
  --verbosity minimal
```

The TRX counters independently confirm total/executed/passed 1304, failed and
notExecuted zero. The separate `Klikety.SmokeTests` suite was **not run on the
shared desktop**; it includes live input/hotkey operations and is not ordinary CI.
The [bounded integration review](reviews/pre-merge.md) found no high-confidence
blocking code findings. Its active result and the full-suite/build markers passed
`Invoke-DirectEvidence`; native checks remain distinct.

User acceptance ("looks good, settings now work") establishes the observed
production tray/Settings opening and accepted presentation. Exact executable/PID
and responsive-window inspections establish a running production instance.
Neither proves fixture-only reuse, all-page keyboard/error announcements,
registration reassignment/recovery, or any DPI row. No unchecked criterion was
closed from that acceptance.

Installed `Archive-Plan.ps1 -WhatIf` refused with the exact message:
`Cannot archive plan '0b37c2': incomplete (2/14 steps complete).`
The user subsequently chose "Hold merge until native checks and plan
finalization allow archival". No push, archive move, criterion change, terminal
review, learning handoff or finalization is claimed. Step 6.1 still depends on
5.3; all earlier unchecked prerequisites remain in place.

The user authorized an already available Windows Sandbox for isolated native
verification. Read-only discovery found `WindowsSandbox.exe`, enabled
`Containers-DisposableClientVM` (`Win32_OptionalFeature.InstallState=1`) and a
present hypervisor on Windows 11 Pro. The non-admin DISM query reported
`The requested operation requires elevation.` Availability is not yet evidence of
guest startup or DPI capability. No feature installation/enabling or host DPI
change was performed. An offline self-contained Release fixture bundle was
published successfully after restoring its missing win-x64 assets into owned
session outputs.

## Identity and admission

- Plan: `0b37c2`; confirmed criteria unchanged.
- Baseline: `6b4afb868ff4d732be6edc7bb9925d725d960120`.
- `Test-PlanCriteriaBaseline`: `ready`, same baseline and confirmed digest.
- Implementation: `6dd7650bd6d0b4272069086c6265749c03fd6b03`.
- Reviewed/tested source, including failed-hook dispatch correction:
  `300e9d19fe5c2359b9c103b6cce88aaa4e58cfa5`.
- Branch: `jiri-san-settings-ui-implementation`.
- Workspace: `C:\Users\jiri\root\dev\copilot-worktrees\klikety\jiri-san-sturdy-dollop`.
- No live runtime fixture, global-registration exercise or DPI change on the shared
  desktop; no production AppData/registry mutation, unrelated process termination,
  push, PR, merge or worktree cleanup.

## Observed validation

Release tests passed **270/270**, zero failed/skipped:

```powershell
dotnet test .\src\Klikety.Tests\Klikety.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~Settings|FullyQualifiedName~AppPathsTests|FullyQualifiedName~ConfigLoaderTests|FullyQualifiedName~MacroConfigValidationTests|FullyQualifiedName~ThemeLoaderTests|FullyQualifiedName~MacroPlayerTests|FullyQualifiedName~KeyPressDisplayManagerTests|FullyQualifiedName~CoordinatorModeSwitchingTests'
dotnet build .\src\Klikety\Klikety.csproj -c Release --no-restore
```

The production build passed with zero warnings/errors. No dependency changes/restores
were needed. Raw TRX is retained in this implementation session's `files` directory as
`settings-release.trx`; it is not a repository or native-runtime evidence file.
The filter matched these classes:

| Class | Passed |
|---|---:|
| SettingsApplyTests | 31 |
| SettingsRecoveryTests | 10 |
| SettingsConfigStoreTests | 32 |
| SettingsDraftTests | 3 |
| SettingsCoverageTests | 1 |
| SettingsRoundTripTests | 5 |
| SettingsValidationTests | 44 |
| SettingsMigrationTests | 3 |
| SettingsIsolationTests | 14 |
| SettingsKeyEditorTests | 12 |
| SettingsNavigationTests | 4 |
| SettingsWindowTests | 1 |
| SettingsAccessibilityTests | 1 |
| AppPathsTests | 3 |
| ConfigLoaderTests | 50 |
| MacroConfigValidationTests | 15 |
| MacroPlayerTests | 25 |
| KeyPressDisplayManagerTests | 9 |
| CoordinatorModeSwitchingTests | 7 |

`Invoke-DirectEvidence` passed all 19 test-class markers, `test:ReleaseBuild`,
`file:docs/design-notes/settings.design.md#exists` and `review:cr`. The review used the
active in-memory result, exact source and resolved scope; its persisted
[phase-2 report](reviews/phase-2.md) is historical output, not evidence authority.
This is one non-terminal, risk-selected direct runtime review with one corrective
replacement for the changed failed-hook dispatch scope, not the final whole-plan CR.
No delegated agents were used.

`git diff --check` and the local installed high-confidence secret guard passed.
The broad suite was not rerun; earlier timing-sensitive results are not claimed stable.

## Per-step implementation and closure

| Step | Verified machine work | Whole-step state / remaining gate |
|---|---|---|
| 1.1 | Scalar and missing-object effective defaults, strict input, migration, JSONC/BOM/unknown preservation, no-op/conflict/I/O/replace cases | Checked; existing completion retained |
| 1.2 | Seven-page reusable WPF draft, page retention, dirty/revert, General editing, confirmation cancel/discard, error retention and repair | Unchecked: actual tray reuse and native keyboard workflow |
| 2.1 | Shared captured-model replacement and save/recovery engines; independent disk/runtime baselines and restoration outcomes; idle/reentrant/concurrent guard; every main/scroll/macro preflight; partial logger/window/coordinator/hook/HUD acquisition; reverse release, retained failed-cleanup ownership/retry; prior logger lifetime; HUD/pause restoration; original backup protection | Checked: its fake/file contracts and prerequisite 1.1 passed; real native registration remains mandatory under 5.3 |
| 2.2 | Actual editor Save/reopen and retained invalid/apply-failed draft; independent recovery combinations, precommit failures, external divergence, disabled retry, repaired input and successful rebase | Unchecked: prerequisite 1.2 and actual tray/runtime consistency |
| 3.1 | Action/list add/move/remove Save/reopen, all six actions, duplicate draft rejection, same-container orphan comments and untouched element spelling; stable physical capture/modifiers/repeats/Escape/focus cancellation and unsupported picker fallback; full collision validation | Unchecked: prerequisite 2.2; native capture/keyboard observations recorded below |
| 3.2 | Every mode flag/chord/default/size and nullable scope round-trip; UniformGrid applicability and chord regression; non-QWERTY fallback/config preservation/one warning; invalid combinations reject | Unchecked: prerequisite 3.1 |
| 3.3 | Built-in/custom theme reference versus external-color advisory, numeric label floor, named diagnostics/count validation, metadata byte retention, real fixture logger I/O failure and simulated recovery | Unchecked: prerequisite 2.2; native theme/logger refresh belongs to 5.3 |
| 4.1 | Scroll enabled/hotkeys/amount round-trip and collision checks; registration-stage faults; preserved pause/disabled transition and shared cleanup/recovery | Unchecked: prerequisite 3.1; actual scroll/tray behavior |
| 4.2 | All macro/indicator fields, nullable global hotkey, ordered slot operations, disabled/null repair, finite nonnegative speed including zero, edited schema floors/untouched legacy warnings, construction safety and separate-file isolation; recording/playback idle rejection without timing sleeps | Unchecked: prerequisite 3.1; actual macro registration/indicator behavior |
| 4.3 | All ten HUD fields and validation; active/off replacement, failed refresh/recovery cleanup, single resource generation and runtime-only enable boundary | Unchecked: prerequisite 2.2; actual enabled/off HUD and native hook/window cleanup |
| 5.1 | Explicit map of all 69 editable serialized leaves; every editor driven to Save/reopen; metadata/extensions/BOM/comments, nullable/disabled/migration cases, full file/resource failure matrix and separate fixture files/path checks | Unchecked: prerequisites 3.x/4.x; no native results inferred |
| 5.2 | Automation peer names and tab stops across all pages; category arrows/focus traversal, capture focus cancellation, action/list focus retention and validation target; collapsed Advanced and unclipped logical bounds at 980/1080/1280 DIP widths; status live-region change event wiring | Unchecked: prerequisite 5.1 and actual keyboard/announcement/DPI matrix |
| 5.3 | Executable fixture controls and operator procedure implemented; one-shot candidate/recovery faults independent, guarded external-edit case, explicitly operated real conflict reservation/release | Pending human: actual isolated host/display exercise |
| 6.1 | Matching design-note maintenance and production guidance supplied, with prototype history kept separate | Unchecked: explicit prerequisite 5.3; final reconciliation still gated |

The machine portions are implementation/testing, not substitutions for native rows.
Unchecked prerequisites prevent whole-step closure, not continued safe automation.
No remaining safe pre-human implementation/test gap was identified in this checkpoint.

## Native operator evidence still required

Use the exact [isolated operator procedure](../../../design-notes/settings.design.md#native-operator-procedure-plan-53-not-automated-evidence).
A separate Windows host/display must establish absolute nonlinked fixture paths and free
Ctrl+Alt+Shift+F11/Pause, conflict Backspace and any enabled scroll/replacement shortcuts.
Do not launch this runtime fixture on the shared desktop.

Collect early native evidence in prerequisite order: 1.2 tray/General workflow, then
2.2 actual Save/apply/recovery, then relevant 3.x/4.x observations and 5.1 closure.
Collect 5.2 keyboard/accessibility/scaling evidence before closing 5.3's full exercise.
The fixture can be used to gather those earlier rows; this does not prematurely
close the formal 5.3 step.

| Native row | State |
|---|---|
| Actual tray opens/focuses one Settings window; edit/save/reopen every category | Unverified |
| Real main/scroll/macro registration, activation, owner reassignment and conflict refusal without disk mutation | Unverified |
| Real candidate/recovery/file faults, retained draft/backup, independent partial results and preserved newer external bytes | Unverified |
| Actual busy navigation/recording/playback refusal, HUD enabled/off refresh and scroll pause/tray state | Unverified |
| Native key capture/layout labels/cancel and keyboard-only edit/Advanced/list/error/save/discard/close/quit on every page | Unverified |
| Screen-reader/UIA status announcement and visible focus | Unverified |
| 100% scaling, work area >=1280x720 DIP, scroll access and unclipped actionable controls/footer | Unverified |
| 150% scaling, work area >=1280x720 DIP, scroll access and unclipped actionable controls/footer | Unverified |
| 200% scaling, work area >=1280x720 DIP, scroll access and unclipped actionable controls/footer | Unverified |
| Fixture Quit releases only its resources/registrations; production files and user's app unchanged | Unverified |

Final whole-plan CR, design-note compaction, recent-learning handoff and archival are
not run while these required rows remain unavailable. The confirmed plan remains active;
this is a pre-human handoff, not whole-plan completion.

## Manual-feedback color component checkpoint

The user selected a small WPF RGB/opacity dialog rather than the RGB-only Windows
ColorDialog. All four existing HUD/indicator colors now offer it alongside direct hex
input. This bounded UI choice does not change confirmed config/persistence criteria.

Release validation passed **88/88**, zero failed/skipped: `SettingsColorDialogTests`
(30), `SettingsAccessibilityTests` (1), `SettingsWindowTests` (7), and
`ConfigLoaderTests` (50). The production Release build passed with zero warnings/errors.
Both used a separate session-artifact `BaseOutputPath` to avoid the running preview's
apphost lock; no dependency/project-file changes were needed.

The new component checks cover six/eight-digit exact representation, RGB/alpha
extremes and synchronization, unchanged/reverted selection, invalid input blocking
and explicit repair, checkerboard preview, actual owned modal OK/Cancel, named/tabbable
controls and bounded logical layout. Each of the four real editors covers cancelled,
unchanged, invalid-current/rejected-result and accepted selections, disk isolation
until Save, and the persisted color after reopening through the strict store.
Existing all-page logical layout/name checks and editor round trips remain passing.

`Invoke-DirectEvidence` passed the four test-class markers, `test:ReleaseBuild`, and
the component/design-note existence markers. `git diff --check` and the local
high-confidence secret guard passed. No new review verdict or native evidence is
claimed from this UI update; the earlier runtime CR remains historical at its source.

The hook-free demo remains manual inspection only. Actual native runtime/DPI evidence,
unchecked prerequisite steps, 5.3, 6.1 and whole-plan finalization stay gated.

## Manual-feedback modifier dropdown and system theme checkpoint

The user requested independent modifier selection and confirmed that Windows
light/dark should affect Settings and the color dialog only, not the configured
navigation-overlay theme. All four modifier fields now share a typed checkbox
dropdown. Both windows use window-local Fluent System mode with dynamic surfaces,
text, borders and status; no application theme, config field, OS preference or
overlay resource is changed.

Focused Release validation passed **98/98**, zero failed/skipped:
`SettingsModifierPickerTests` (3), `SettingsThemeTests` (1),
`SettingsAccessibilityTests` (1), `SettingsWindowTests` (7),
`SettingsColorDialogTests` (30), `SettingsKeyEditorTests` (12), and
`SettingsValidationTests` (44). Production Release build passed with zero
warnings/errors. Output is the separate session-artifact `settings-modifiers-theme`
directory; no new dependencies, project changes or unrelated suite reruns.

The dropdown tests verify all sixteen combinations, independent checkbox toggles,
unknown-value visibility/explicit repair, readable summary and UIA patterns,
multiple selections without dismissal, Space/arrows/Home/End/F4/Escape/Tab,
disabled/unloaded popup cleanup, exact popup width, and strict save/reopen with
untouched overlay theme/comments/unknown scalar text. The editor field map still
drives every main/scroll/nullable-macro field. A keyboard regression exposed Space
dismissing the popup; explicit non-repeating focused Space handling keeps it open.
Closed popup controls are checked for names but measured in their own popup tree
when open, not falsely treated as visuals inside the page.

Theme tests show the fixture windows and force Light/Dark/Light only on those
windows. They check actual backgrounds, foregrounds, cards and normal/error
resources while retaining unsaved edits, config bytes and overlay reference.
An unrelated window keeps None theme mode. This is not desktop preference-change,
screen-reader announcement or native DPI evidence.

`Invoke-DirectEvidence` passed the seven test-class markers, `test:ReleaseBuild`,
and modifier/design-note existence markers. Local secret guard and diff hygiene
passed. Native prerequisites, 5.3, 6.1, final CR/compaction/learning/archival remain
pending; no new completed whole-step or review result is claimed.

## Fluent control contrast correction

User inspection found black checkbox/Advanced text and legacy light buttons on
dark surfaces. A new regression reproduced `Dark page 0 CheckBox 'Ctrl modifier':
#FF000000 text`; button coverage also rejected legacy `#FFDDDDDD` against the
Fluent `#B3FFFFFF` background. The previous window-palette checks were insufficient.

Both window resource dictionaries now explicitly import Microsoft Fluent before
local styles, and implicit sizing overrides inherit the named `Default*Style`
keys, not their own implicit type keys. This corrects checkbox/expander text,
button/dropdown/input styling and the color dialog without hard-coded white-text
patches or a new dependency. The keyed slider override avoids the same ambiguity.

Release checks passed **42/42**: theme (1), all-page accessibility/layout (1),
modifier dropdown (3), real editor (7), and color dialog (30). The Release build
passed with zero warnings/errors, using separate `settings-theme-contrast` output.
Light/Dark/Light checks now cover actual control foregrounds across all seven
pages, color-dialog labels/inputs/buttons, Fluent button backgrounds and composited
enabled text contrast >= 4.5:1, while retaining draft/config/overlay-theme state.
Native template changes also retain keyboard traversal, popup cleanup and bounds.

Direct evidence passed those five test markers, ReleaseBuild and the design-note
marker. Local secret guard and diff hygiene passed. This corrects the prior theme
checkpoint; it still does not close native runtime/DPI gates or finalization.

## Production tray integration verification

The user accepted the inspected UI and requested full application integration.
The existing production `App.SetupTrayContextMenu` already included **Settings...**
as its first item, wired to the same editor with actual captured-model validation,
apply and recovery. Repeated clicks activate/restore the existing window; closing
clears the reference. No duplicate menu entry or separate prototype editor was added.

Integration inspection found a fixture routing defect: `ShowSettings` used
`UserConfigPath` whenever no demo filename was supplied, including runtime fixtures.
It now resolves the file from the same captured `AppPaths` root as the runtime.
The shared resolver also preserves nonstandard explicit demo filenames during
runtime-snapshot reads, rather than reading a different root `config.json`.

Focused Release tests passed **67/67**: tray command (1), AppPaths (4), editor (7),
apply (31), recovery (10), and isolation (14). Production Release build passed with
zero warnings/errors at `src/Klikety/bin/Release/net10.0-windows/Klikety.exe`.
The tray-item factory regression checks actual label/name/Click dispatch; path
regression checks normal/fixture/demo routing without reading or writing AppData.
Matching docs and the README tray inventory now identify Settings consistently.

Direct evidence passed those six test markers, ReleaseBuild and the config
design-note marker. Local secret guard and diff hygiene passed. User UI approval
is not substituted for actual production shell reuse, registration/recovery,
keyboard/announcement/DPI evidence. No production app or live runtime fixture was
launched on the shared desktop; all unchecked steps/finalization remain gated.

## User-directed main merge and config v8 compatibility

The user requested fetching and merging `main` because configuration had advanced.
Fetched `origin/main` at `e1073ea`; integrated keyboard help/config v8 and the
completed maintenance corrections. Resolved overlapping indicator/hook code by
keeping main's dispatcher-owned indicator cancellation/cleanup and modifier
capture, together with Settings' strict indicator validation and disposed-hook
dispatch quiescence. Both design-note entries/contracts were retained.

Settings' reader still pinned config v7 even after the migrator/model advanced.
It now checks `ConfigMigrator.CurrentConfigVersion` without migrating during editor
Open/Preview/Save. Current fixtures use v8; historical v7 and future v9 remain
rejected unchanged, and migration-before-snapshot coverage includes v7 -> v8.
The three new help-binding fields are editable on Key bindings and included in
the explicit whole-model map, actual editor checks, round trips and preservation
checks. Help failures focus that picker. Shared policy now covers Uniform-grid
chords and lets null macro slots reach their explicit validation error instead
of throwing while checking help. Disabled invalid physical keys still block Save.

The first merge-contract run exposed missing help coverage and three old migration
expectations. After integration, the selected Settings/AppPaths/config/help/
indicator contracts passed **356/356**, zero skipped. Production Release build
passed with zero warnings/errors. Direct evidence covers current Settings coverage,
migration, validation, store, round trip, editor, apply/recovery, isolation, merged
config/help/indicator contracts and build. Baseline admission remains ready with
unchanged authoritative intent/requirements/risks/decisions. No criteria/checklist
marks, whole-plan completion, native runtime/DPI gate or archival were inferred.

The earlier production launch and console diagnostic process both exited; the
diagnostic process reported exit code 0 without an exception trace. The v7-only
reader defect is verified; the reported disappearing-window symptom is not yet
claimed retested through the updated native tray path.

## Footer Close and user tray confirmation

The user confirmed "looks good, settings now work" on the updated production build;
the earlier disappearing-window report is no longer an open symptom. This does
not establish the remaining isolated registration/recovery or native DPI criteria.

User requested a **Close** button beside **Save & apply**. Added an accessible
Alt+C footer action that calls `Window.Close()` and reuses the existing Closing
confirmation. It does not save/apply or introduce a new Escape behavior. Fixture
checks cover clean immediate close, dirty/pending-apply cancellation and acceptance,
no close-time disk/runtime mutation, accessible name and tab traversal from Save.
Logical layout coverage includes the new button beside Save at 980/1080/1280 DIP;
the existing light/dark contrast checks include it as an enabled control.

Focused Release editor/accessibility/theme checks passed **12/12**, zero skipped;
Release build passed with zero warnings/errors. Matching direct test/build/file
evidence and local guard/diff hygiene passed. Checklist/native/finalization gates
remain unchanged.

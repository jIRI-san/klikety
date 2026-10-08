---
description: Reproducible native README captures in an offline Windows Sandbox, guarded demo input, DPI and lossless APNG output.
globs:
  - .github/skills/capture-demo/**
  - scripts/readme-demo/**
  - docs/screenshots/**
  - README.md
---

# README Demo Captures

The README gallery uses the production app and bundled UIA worker over a separate
WinForms project dashboard containing synthetic data. All desktop capture and
keyboard injection happen inside an owned Windows Sandbox, never on the host.
These are native application screenshots, not composed renderer mockups.

## Reproduce

Invoke the repo skill `/capture-demo`, or run the complete pipeline directly.
Requires Windows Sandbox with `wsb.exe`, an interactive guest connection, the
.NET 10 SDK, Node.js 22+, installed Edge/Chrome and ordinary publish dependencies.
Run from the repository:

```powershell
.\scripts\readme-demo\Capture-Demo.ps1
```

The default chooses a unique temp directory outside the checkout; optional
`-OutputDirectory` names an unused evidence directory. The runner publishes self-contained win-x64
app/probe packages into that directory, using separate build outputs so a running
host Release app is not overwritten. No guest SDK, network or runtime installation
is needed. A `LogonCommand` starts capture after the guest login; the runner waits
up to three minutes, reports guest errors and stops only its own Sandbox GUID.
Packages, native captures, metadata and logs remain in the output directory for
inspection. The complete pipeline verifies the capture and APNG playback, then
publishes only the eight images and `capture-info.json` to `docs/screenshots`.
No commit, push or PR is created.

Only the staged input folder is mapped read-only; only the capture folder is
writable. Networking, clipboard, microphone, video and printer redirection are
disabled. The repository, host AppData and home directory are not shared.

Use `-NoPublish` to capture/verify without replacing gallery files. To recover
after capture succeeded but verification/publication failed, use
`-CaptureDirectory <old-output>\captures` with a new `-OutputDirectory`.
`-BrowserPath` selects a nonstandard installed Edge/Chrome executable. Missing
tools/browser fail preflight; nothing is installed automatically.

The original `Capture-ReadmeDemo.ps1 -OutputDirectory <unused-directory>` remains
the capture/encoding-only runner. `Capture-Demo.ps1` adds verification and
publication without duplicating guest automation.

Inspect every mode image and animation frame for correctness, clipping and
legibility. Keep diagnostics, packages and intermediate frames out of Git.
Never replace a failed or loading-only capture with a success-shaped image.

## Automatic Verification and Publication

`Verify-Demo.mjs` uses Node built-ins only. It requires the guest completion marker,
no error marker, complete discovery, three preserved combo-box group controls, consistent
native PNG dimensions and all six raw grid frames. It checks PNG/APNG CRCs, sequence
numbers, full-canvas replacement, true color, distinct frames and the exact
1.4-second delays, plus local Markdown targets/anchors. Each of the eleven encoded
frames must match its individually rendered reference PNG in the declared order.

A fresh headless Edge/Chrome profile connects only to an ephemeral localhost
fixture. Secure-context `ImageDecoder` must decode all eleven animated frames; actual
browser screenshots must change across a frame boundary. `Canvas.drawImage`
is not used as playback evidence. The browser/server are closed in `finally`;
only the owned profile is removed. Browser diagnostics/screenshots stay outside Git.

Only verified assets are published. The wrapper compares pre-run gallery hashes
to refuse concurrent edits, saves existing files under `previous-gallery`, copies
the exact verified bytes and checks their recorded hashes. Changed source captures
are rejected before publication. Validation failures leave the
gallery unchanged. Publication consists of separate file copies, not a multi-file
atomic transaction; disk errors are surfaced and the backup is retained.

`docs/screenshots/capture-info.json` records each run's UTC times, native size,
fixture DPI, hierarchy counts, image hashes, APNG contract and browser proof.
It excludes process IDs, host paths and provider names/values. README links and
captions remain stable; the skill updates them when behavior changes.

Run focused helper regressions with:

```powershell
node --test .\scripts\readme-demo\Verify-Demo.test.mjs
```

The [repo skill](../../.github/skills/capture-demo/SKILL.md) also records the
login/foreground/DPI debugging lessons and the complete inspection/finish procedure.

## Runtime and Input Boundaries

`DemoApplication.ps1` and `CaptureGuest.ps1` reject non-Sandbox accounts.
The fixture exposes a ready HWND/PID after `Shown` and uses physical-pixel DPI
coordinates. The controller verifies the fixture PID, checks guest foreground
ownership before input/capture, and stops on unrelated foreground changes.
Fixture-only activation can temporarily attach the controller and foreground
input queues; it never changes production foreground/action guards.

The guest's separate `%APPDATA%\Klikety` uses the normal production startup path.
The capture config uses Ctrl+Alt+Shift+F11 for activation, disables macros and
enables ElementHints with a 10000 ms discovery deadline. Mode chords, axes, theme
and navigation behavior stay at their first-run defaults. The host app/config
are untouched. The demo sends label, mode and Escape keys, not mouse-action keys,
clicks, drags or scroll commands. Cursor previews are ordinary production moves.

`HintLabelsProbe` links the production protocol, supervisor and pure hierarchy
planner. It scans only the ready fixture with the published worker, in physical
coordinates, and selects a compound group's real one/two-key label. It never
validates or dispatches an action. Production footer text must confirm completed
L1 discovery and L2 entry before the hint screenshots are accepted.

The fixture provider is explicitly warmed before capture. This is illustrative
warm-condition evidence, not a cold-start benchmark or a claim that the default
1500 ms discovery budget works in every application.

## Assets and Animation

| Asset | State |
|---|---|
| `uniform-grid.png` | Initial 10×10 grid |
| `uniform-grid-zoom.png` | Local grid after J then T |
| `crosshair.png` | N chord, uniform axes |
| `log-crosshair.png` | M chord, logarithmic cross |
| `log-grid.png` | Comma chord, logarithmic two-key grid |
| `element-hints.png` | Real foreground UIA control labels and `+` groups |
| `element-hints-children.png` | One-key combo-box/child badges with matched colors and border patterns |
| `navigation-demo.png` | Captioned APNG: all five modes, nested ElementHints, then grid refinement |

Native PNG stills retain full guest resolution. `Encode-Apng.ps1` resizes animation
frames to 960 pixels wide, adds a 40-pixel caption band outside the captured
desktop, converts them to RGBA PNG and writes standard APNG
`acTL`/`fcTL`/`fdAT` chunks with CRCs. Eleven full replacement frames use 1.4-second
delays and infinite looping. Compression preserves true color; no GIF palette
quantization or external encoder is used. The first frame remains an ordinary
PNG for static/older viewers. The `.png` extension gives the README a conventional
PNG image URL while its animation data remain APNG.

`AnimationFrames.json` is the shared fixed storyboard, not a user setting.
It starts with UniformGrid, Crosshair, LogCrosshair, LogGrid and ElementHints L1/L2,
then continues the remaining five grid-refinement states. This is a captioned
montage of actual captures, not a continuous recording or fabricated app UI.
The six original raw grid frames and all native mode stills remain unchanged.
Individually rendered `animation-frames/animation-frame-*.png` stay with capture
evidence outside Git and let verification detect missing/reordered frame content.
When reusing completed captures, the wrapper copies them to its new output
directory and re-encodes with the current storyboard, preserving old evidence.

[Current Chrome, Edge, Firefox and Safari support APNG](https://caniuse.com/apng),
including their modern mobile browsers. Keep a static-view link beside the README
animation; embedded animations do not provide a universal pause/reduced-motion
control. Browser rendering is checked separately from native UI capture.

## Published Capture Evidence

The initial gallery capture produced seven native stills at 3046×1650
with the fixture reporting 192 DPI (200%). Production discovery retained 47
targets; the matching planner produced 20 L1 entries and the combo-box group
preserved three targets in its one-key L2 picker. The fixture and probe both use
PerMonitorV2 awareness; WinForms scales the completed layout from its 96-DPI
design dimensions before showing it.

Its 960×520 APNG contained six CRC-valid full-canvas frames at 1.4 seconds each.
An isolated Edge instance decoded all six animated frames and rendered different
images across a frame boundary. This checks actual browser playback, not
`Canvas.drawImage`, which can expose only the default frame. GitHub's rendered
README was not exercised; the gallery uses ordinary relative PNG image links.
The runner stopped its owned Sandbox and left the running host app untouched.
These figures describe that initial capture, not every future display/provider.
The published per-run manifest supplies current figures after `/capture-demo`.
The refreshed L2 presentation uses control-associated badges and redundant
border patterns instead of the initial role list. Inspect the native still for
badge/outline/leader associations as well as the production L2 footer; metadata
checks alone do not establish visual legibility.
The animation was subsequently expanded from the original UniformGrid-only six
frames to eleven captioned frames covering every mode and nested hints. Native
stills can be reused when only this presentation changes; the per-run manifest
records the current sequence and animation hash.

## Evidence Limits

The gallery shows appearance, grid refinement and nested label navigation.
It does not demonstrate successful physical clicks/drags, third-party UIA
compatibility, Word cold-start discovery, mixed monitors or the full DPI/input
acceptance matrix. Existing [ElementHints safety](element-hints.design.md) and
[testing gates](testing.design.md) remain unchanged.

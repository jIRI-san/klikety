---
name: capture-demo
description: Capture and refresh Klikety's README demos automatically using the production app in an offline Windows Sandbox, native PNGs, a verified APNG and reproducible provenance.
user-invocable: true
disable-model-invocation: true
---

# Capture Demo

Run `/capture-demo` to repeat the complete capture-to-gallery process in the
current checkout. Execute directly; no subagents, separate worktree, commits,
pushes or PRs are needed. Preserve unrelated dirty files and the running host app.

## Load and check

Read `docs/design-notes/.design-notes.md`, then
[`readme-demo.design.md`](../../../docs/design-notes/readme-demo.design.md).
Check current `README.md`, the shipped config and existing gallery before editing.
The repository must be on Windows with .NET 10 SDK, `wsb.exe`, an interactive
Sandbox connection, Node.js 22+ and installed Edge or Chrome. Do not install a
browser, enable Windows features or change host config silently.

## Run the whole process

From the current repository, run:

```powershell
.\scripts\readme-demo\Capture-Demo.ps1
```

The default uses a fresh directory outside the checkout and automatically:

1. Preflights tools/browser and snapshots the existing gallery.
2. Publishes isolated self-contained app and read-only probe packages.
3. Starts an owned offline Sandbox with narrow read-only input/writable capture
   mappings and a guest login command.
4. Runs the DPI-aware synthetic dashboard and production app; captures all five
   modes, UniformGrid zoom, real one-key L2 control badges and six navigation frames.
5. Encodes the captioned all-mode APNG and stops the owned Sandbox.
6. Checks completion, complete discovery, the three preserved combo-box controls,
   PNG dimensions/CRCs, APNG sequence/frames/timing and local documentation links.
7. Decodes all animated frames in an isolated headless browser and compares real
   rendered frames; closes that browser/server and removes its temporary profile.
8. Backs up the previous gallery, refuses concurrent asset edits, publishes the
   eight images plus `capture-info.json`, and checks copied bytes.

For a named evidence directory or nonstandard installed browser:

```powershell
.\scripts\readme-demo\Capture-Demo.ps1 -OutputDirectory C:\temp\KliketyDemo-001 -BrowserPath 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
```

`-NoPublish` performs capture and verification without replacing repository assets.
If capture completed but verification/publication failed, reuse the retained
guest output rather than recapturing:

```powershell
.\scripts\readme-demo\Capture-Demo.ps1 -CaptureDirectory C:\temp\KliketyDemo-001\captures -OutputDirectory C:\temp\KliketyDemo-verify-001
```

Reuse copies the completed captures into the new output directory and re-encodes
the current storyboard; original capture evidence is not overwritten.

Every output directory must be unused. Never reuse old `fixture.json`, skip a
failed gate or publish a fixture-only/loading image. Failures retain diagnostics
and leave the gallery alone until publication; a disk error during copying is
reported and the previous files remain in `previous-gallery`.

## Inspect and finish

Inspect all seven stills and the six `frame-*.png` images for mode correctness,
readability, clipping, badge placement and the L2 badge/outline/footer associations.
Check matched colors, redundant solid/dashed/dotted patterns and leaders when
badges are displaced; do not treat color alone as accessibility evidence. The APNG must
remain `.png`, 960 pixels wide, eleven full replacement frames, 1.4-second delays,
infinite looping and static-first-frame fallback. Keep the README's static link.
Inspect all eleven `animation-frames/animation-frame-*.png` renders too. The
shared `AnimationFrames.json` must cover every mode, nested hints and all six grid
states; captions occupy a 40-pixel band outside the captured desktop. The verifier
checks each encoded frame against its ordered rendered reference.

Check captions, current mode chords/defaults and relevant design notes against
the implementation. If a new mode or changed flow needs capture changes, update
the fixture/controller, verification expectations and gallery together, then rerun.
Do not update historical acceptance claims or claim GitHub playback was tested
from local browser evidence. Use `capture-info.json` for each run's real DPI,
geometry, counts, hashes and browser evidence rather than hardcoded old results.

Run the focused helper checks and inspect the final diff:

```powershell
node --test .\scripts\readme-demo\Verify-Demo.test.mjs
git --no-pager diff --check
```

Report the refreshed gallery and evidence location concisely. Keep packages,
logs, raw frames, probe metadata, backups and browser diagnostics outside Git.
Do not stop unrelated Sandboxes/browsers or rebuild over the host's running
Release output. No host keyboard/mouse injection, desktop screenshots or AppData edits.

## Known pitfalls and preserved decisions

- `wsb start --config` accepts XML, not JSON/a filename. `ExistingLogin` can race
  guest login; keep `LogonCommand` and one interactive connection.
- Foreground acquisition can be denied. Keep fixture-PID checks and the narrow
  guest-only input-queue attachment; never weaken production guards.
- UIA bounds must be physical: use PerMonitorV2 in fixture/probe, clip root bounds
  to the screen, and autoscale the finished WinForms layout from 96 DPI.
- Capture full `Screen.Bounds`; `WorkingArea` crops the hint footer.
- Warm the controlled provider and use the guest's 10000 ms discovery budget.
  This is not default-1500-ms, Word, cold-start or universal-provider evidence.
- The probe selects a real compound group's production-planned keys; do not
  hardcode an L1 label or mistake cursor preview for L2 entry. Gate both levels
  on the actual production footer.
- `Canvas.drawImage` may show only an animation's default frame. Keep secure
  localhost `ImageDecoder` plus real browser screenshots for APNG verification.
- Prefer installed Edge/Chrome over a missing tool-managed Chrome distribution.
  Use a fresh browser profile, ephemeral localhost ports and owned-process cleanup.
- Send navigation/mode/Escape keys only. No click, drag, scroll or action keys.
  Warm rendering evidence does not close physical-input or mixed-DPI gates.

All supporting source lives in [`scripts/readme-demo`](../../../scripts/readme-demo):
`Capture-Demo.ps1`, `Capture-ReadmeDemo.ps1`, `CaptureGuest.ps1`,
`DemoApplication.ps1`, `HintLabelsProbe`, `Encode-Apng.ps1`, `AnimationFrames.json`,
`Verify-Demo.mjs` and its focused tests. No session-local script is required.

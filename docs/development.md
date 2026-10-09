# Development

[Documentation](README.md) | [Getting started](getting-started.md)

Build on Windows 10 or later with the **.NET 10 SDK**. Run these commands from
the repository root:

```powershell
dotnet build Klikety.slnx
dotnet run --project src\Klikety\Klikety.csproj
```

Normal `dotnet run` starts the real tray app with the user's AppData config and
global hotkeys. Use a separate isolated environment for native input testing;
do not disrupt an existing Klikety instance.

## Publish

```powershell
dotnet publish src\Klikety\Klikety.csproj -r win-x64 --self-contained -c Release
```

Deploy the whole `publish` folder, including `uia-worker`. The main project builds
and bundles the separately owned UIA helper; copying only the executable is not
a complete installation.

## Tests

```powershell
# Managed unit, integration, worker, and STA rendering checks
dotnet test src\Klikety.Tests\Klikety.Tests.csproj

# Native smoke checks: requires an interactive desktop, not CI-safe
dotnet test src\Klikety.SmokeTests\Klikety.SmokeTests.csproj
```

Settings UI tests inject apply/recovery callbacks and use temporary files; they
do not install global hooks or use the user's config. Running the ordinary suite
does not establish live tray/input, third-party UIA, or native DPI acceptance.
See [testing contracts](design-notes/testing.design.md).

## Isolated Settings runtime fixture

The hook-free `--settings-demo` mode has been removed. Old invocations report an
error and exit before normal startup; they must not silently launch against the
user's config. Use ordinary tray Settings for editing or the runtime fixture for
controlled registration/recovery checks.

On a **separate safe Windows host/display**, choose a dedicated absolute folder
outside the Klikety AppData tree and its ancestors, with no reparse points:

```powershell
dotnet run --project src\Klikety\Klikety.csproj -c Release -- --settings-runtime-fixture C:\temp\KliketySettingsFixture
```

This launches actual application services with config, logs, macros, themes,
and topology confined to the fixture root. It skips normal first-run extraction
and the startup registry toggle, disables the unrelated Debug shortcut, and uses
**Ctrl+Alt+Shift+F11** / **Ctrl+Alt+Shift+Pause** for main/macro shortcuts.
It registers real system hotkeys. Confirm those combinations and any enabled
scroll/replacement shortcuts are free; do not stop the user's app to free them.
Identify the fixture PID and **ISOLATED RUNTIME FIXTURE** tooltip before operating.

The **Fixture: fail next operation** tray submenu arms one-shot candidate/recovery
faults, including logger, overlay, registrations, HUD, disk save/restore, and a
newer external edit. A separate fixture control reserves
Ctrl+Alt+Shift+Backspace to test preflight conflict refusal. Fault files stay
inside the fixture; ordinary operation has no fault injector.

Follow the full [native operator procedure](design-notes/settings.design.md#native-operator-procedure-plan-53-not-automated-evidence)
for recovery, busy/keyboard checks, and backup inspection. Quit only the identified
fixture. Its directory is retained for inspection, not automatically deleted.

Managed contracts and genuine Sandbox registration/recovery observations are
separate evidence. Deferred native interaction and 100/150/200% display rows
remain unverified; removing the old layout demo does not close those gates.

## Demo captures

The README animation and [mode gallery](navigation.md#modes-in-pictures) use
production overlays in an offline owned Windows Sandbox over synthetic data.
The repo-specific [/capture-demo skill](../.github/skills/capture-demo/SKILL.md)
and its supporting runner capture, verify, and publish them:

```powershell
.\scripts\readme-demo\Capture-Demo.ps1
node --test .\scripts\readme-demo\Verify-Demo.test.mjs
```

This is independent of the removed settings demo. Preserve the offline Sandbox,
read-only package mapping, narrow writable capture mapping, and host-input
isolation. See [capture requirements, APNG verification, and provenance](design-notes/readme-demo.design.md)
before regenerating assets. No new capture is needed for documentation-only moves.

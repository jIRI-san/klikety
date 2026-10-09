# Macros

[Documentation](README.md) | [Navigation](navigation.md) | [Settings](settings.md)

Macros record mouse actions into **ten slots**. They store coordinates, modifiers,
and timing, not application text or semantic control identities. Definitions
live in `%APPDATA%\Klikety\macros.json`; Settings edits preferences without changing
those recordings.

## Record and play

With the default bindings, open navigation and press **backslash** to start
recording. Choose a slot using **F1-F10**, then follow the overwrite confirmation
if it is occupied. Navigate and perform the mouse actions you want to record;
the overlay returns after each action. Press backslash again to stop and save.
Escape cancels recording.

Open the picker with **Ctrl+Alt+Shift+M**, or **backtick** while navigation is
active. Press an occupied slot's key to play it. F1-F10 can also play a saved
slot directly while navigation is active. The picker marks screen-relative
macros with `[S]` and window-relative macros with `[W]`.

During playback a progress overlay shows the step count and pending delay,
and a configurable indicator marks action locations. **Escape cancels future
steps**; it cannot undo input already sent. Recording, picking, and playback
are mutually exclusive. Settings cannot apply while macro activity is busy.

## Screen and window-relative recordings

Normal recordings use screen coordinates and validate screen dimensions/DPI
before playback.

Start recording from an **app-scoped** overlay for a window-relative macro.
Coordinates become offsets from that window's top-left. Playback requires a
matching foreground window title pattern, size, and DPI. The window can be
at a different position when playback starts; coordinates use its current origin.
Focus loss or resizing during playback aborts; position changes are tracked
between steps.

After recording a window-relative drag, the **Drag from cursor?** prompt can
store `StartFromCursor`. Such a drag starts at the cursor position captured
when playback begins while its endpoint remains window-relative.

## Timing and editing

**Settings > Macros** controls enablement, picker hotkey, record/helper/slot keys,
global speed modifier, and playback-indicator appearance. Set the global picker
hotkey to `null` to disable that shortcut without disabling overlay-local keys.

The speed modifier multiplies recorded delays: values below 1 shorten waits,
values above 1 lengthen them, and 0 uses a fixed 100 ms wait. Normal scaled delays
have a 50 ms floor. A non-default per-macro speed modifier overrides the global
value. Drags finish before the next step's wait begins.

For names and per-macro settings, edit `macros.json` using `macros.schema.json`,
then Reload from the tray while idle. Back up recordings before manual edits.
Invalid slots are reported and excluded from playback rather than sending
unvalidated coordinates.

Screen/window checks reduce coordinate mistakes; they do not prove that the
recorded controls still have the same meaning. Review the target before replaying
actions that change data. Recording captures attempted actions, including failed
native input, so inspect a recording if an action did not complete.

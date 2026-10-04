# Domain Model

## Terms and meanings

- Pending action: macro step not yet handed to the mouse service; indicator/delay work can be cancelled.
- Input outcome: explicit success or failure with operation/stage, requested and sent counts, available Win32 diagnostic and release-cleanup outcome. A missing OS error explanation is not success.
- Compensation: release-only button/key events following an incomplete batch, not replay of the original action.
- RelativeTimeMs: saved recorder interval before a step, not an absolute timeline offset.
- Canonical plan identity: six-hex handle used by plan lookup, independent of displayed legacy title.

## Actors and boundaries

- MacroPlayer sequences and awaits steps; MacroHandler owns Escape cancellation, playback state and completion reporting.
- WPF dispatcher owns indicator animation and visibility. Cancellation can originate on another thread.
- MouseActionService owns native injection and partial-send policy. Windows may reject input or cleanup.
- ActionDispatcher/coordinator/scroll hotkey callers observe explicit failures without inventing macro playback status.
- Plan inventory/state helpers validate retained documentation; no plugin code changes.

## Interfaces and ownership

- IClickIndicator gains cancellation-aware waiting; adapter owns completion subscription/registration cleanup.
- A pure composed lifecycle seam models dispatch/view operations, completion generations and release cleanup; the WPF adapter supplies production operations.
- IMouseActionService returns typed results; asynchronous drag completion returns an awaitable typed result instead of unobserved Task.Run work.
- MacroHandler initiates nonblocking teardown; the current operation owns eventual resource cleanup, guarded against replacement operations.
- Production sender retains native struct alignment; tests substitute batch outcomes and geometry/delay providers.

## Invariants

- Successful input preserves physical-pixel normalization, ordering and timing.
- Native drag phase timing is preserved; the next saved macro interval begins after drag completion rather than overlapping it.
- A failed prerequisite movement suppresses the dependent click.
- Partial input is not reissued; compensation failure remains explicit.
- Distinct plan records are retained with unique identities; no inferred archive permission.

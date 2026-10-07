# Decisions

- Confirmed: split staggered keyboard halves offset around active screen center; visual tuning delegated within this layout.
- Confirmed: default / ? VKey, Shift optional; custom key/modifier support; not literal-character tracking.
- Confirmed: help is modal for overlay command dispatch, not navigation state; help/Escape closes to the exact selection.
- Confirmed: drag/recording prompts included; separate picker/playback windows excluded; recording clock/global OS hotkeys continue.
- Confirmed engineering choice: existing overlay layer rather than a new activating window; composed model/view, strongly typed projections.
- Operator-selected engineering choices: separate compatible hook modifier flags; explicit key-up ordering; visible-plus-active toggle gate; conservative VKey collisions because existing dispatch is key-only.
- Confirmed visual acceptance: >=12 DIP command text, fit at 800x600 DIP and above, contained scrolling below that size.
- Operator-selected engineering choices: versioned conflict-aware migration disables colliding default help with warning; runtime ignores invalid explicit bindings; no destructive rebinding.
- Operator-selected engineering choices: global picker uses existing hook-disable/suspend handoff from visible navigation; idempotent coordinator help cleanup covers hide/suspend/deactivation/disposal; explicit keyboard-layout and size/DPI refresh paths.
- No new third-party packages or unrelated refactoring.

# Decisions

- Use `IKeyboardLayoutProvider.GetActiveKeyboardLayout`, which targets the foreground window's thread; do not use the current-thread `GetKeyboardLayout(0)`.
- Use `WM_INPUTLANGCHANGE` as the primary overlay signal and a key-down HKL comparison as fallback.
- Keep resolvers immutable and create a new resolver for every detected HKL change.
- Keep label-generator mutation on the UI dispatcher, alongside overlay message delivery and WPF rendering.
- Use renderer-owned label generation for the LogGrid first-key indicator.
- Preserve visuals with last-render-command replay instead of exposing state-machine internals.

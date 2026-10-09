# Domain

- **Window key:** captured HWND, process ID and process-start time. Missing
  identity disables reuse; windows of the same application remain separate.
- **Control key:** process ID plus opaque runtime-ID sequence, with matching
  control type/capabilities. Session tokens and coordinates are not identity.
- **Level key:** root sentinel, or an exact fingerprint of a group's current
  member identities and nested structure. Generated negative group IDs are not
  stable keys. Changed/reparented groups are misses, not migration candidates.
- **Assignment:** level, logical page/slot and single/pair-key scheme.
- **Snapshot:** all assignments allocated in the previous activation, including
  off-page entries and previously visited levels; no UIA objects,
  old actionable targets or accumulated history.
- **Owner:** `ModeSessionFactory`, outside the helper and individual sessions.

Only entries rediscovered in the current activation are rendered/selectable.
Remembered reservations are not controls and never authorize input.

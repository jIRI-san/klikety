# Decisions

- Selected native WPF seven-category sidebar; no new framework/package expected.
  Prototype commit is redesignable input, not completed production work.
- Full known user-editable config scope; General Advanced contains diagnostics.
  Metadata read-only; theme contents/recordings/startup registry/HUD activation out of scope.
- Keep init-only runtime model; separate small typed draft/session and page helpers.
  Reuse shared validators; do not widen this into a general C# refactoring.
- Comment-preserving UTF-8 token edits extend to arrays and member deletion.
  Keep orphan comments in section; normalize only changed text/new fragments.
- Exact-byte optimistic conflict detection and previous-file backup; user explicitly
  accepted the final race with noncooperating external writers. No automatic merge.
- Save/apply at idle only; serialize against reload/reset; no cancellation of navigation
  or macro operations by Settings. HUD enabled state does not prevent apply.
- Failed candidate activation attempts conflict-safe disk/runtime recovery; explicit
  partial failure remains possible. Retain user draft; never overwrite a newer external
  file to restore the old one. Preserve original backup for manual recovery.
- Existing older-config migration precedes editable snapshot; future/malformed input
  blocks Settings writes. Startup compatibility and named Settings blockers stay explicit.
- Keys use readable pickers plus focused capture, stable physical VKey and no new global
  capture hook. Existing reserved/collision behavior remains authoritative.
- Observe keyboard and layout at 100/150/200% with >=1280x720 DIP work area; smaller
  displays remain best-effort work-area adaptation, not an added acceptance promise.
  Verification exception confirmed by the user on 2026-10-08: unavailable native
  DPI and input-dependent scroll/pause/tray, macro activation/playback/busy, active
  HUD/fault/lifecycle/tray, fixture conflict-control and all-page keyboard/status rows
  are deferred to their manual validation, not marked passed. Managed contracts and
  genuine native save/registration/recovery results remain required. This supersedes
  the earlier mandatory-unavailable-row completion hold; finalize/archive and merge
  after the remaining nondeferred gates pass. Exact wording is retained in intent.md.
- History is advisory, current implementation authoritative per user. Filtered index
  reported no intentCandidates/errors; relevant archived 013/014/015 decisions identified.
  Bounded reader returned missing legacy assets, so no accepted historical artifact
  provenance is claimed and legacy plan.md was not loaded outside that reader.
- No implementation, publication, merge or cleanup authorized by this planning session.
  Mandatory two-call pre-confirmation advice occurs once, before final user confirmation.
  This describes original planning admission; later explicit user implementation,
  archival and direct-main authorization is recorded in the finalization correction.
- Review-selected refinements: independent disk/runtime recovery baselines, explicit
  captured-model/path seam, resource/logger ownership, full shortcut inventory, guarded
  original backup, comment-aware collection representation and scalar-first MVP.
- User selected current warning compatibility for LogGrid axis and macro slot lengths;
  no new length-based save blockers. Show effective behavior and finite nonnegative speed.
- User selected existing schema bounds for edited indicator values, retaining untouched
  legacy values with explicit incompatibility/safety handling; no schema relaxation.
  Named log levels/count >=1 are Settings constraints; schema logBaseSize default becomes 10.

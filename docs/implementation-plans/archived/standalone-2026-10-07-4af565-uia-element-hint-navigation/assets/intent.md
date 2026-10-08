# Intent

## Goal

Add a keyboard-label navigation mode that discovers interactive controls through Windows UI Automation, alongside Klikety's coordinate-grid modes.

## Desired outcome

Activate the overlay for the current application, switch to element hints, type a label to select a control, then use the existing mouse-action keys. When the application exposes insufficient accessibility data, use grid navigation without leaving the overlay.

### Selected operator wording and confirmed interpretation

- Source/date: current conversation, 2026-10-07.
- Operator wording: "ok, let's create implementation plan for that using UIA".
- Confirmed scope selection: "Foreground application window only (Recommended)".
- Confirmed reliability selection: "Separate helper process with hard timeout (Recommended)".
- Confirmed interaction selection: "Select the element, then use the existing action keys (Recommended)".
- Interpretation: scan descendants of the application HWND captured before the overlay takes focus; isolate UIA calls in an owned helper that can be terminated; label completion selects without clicking.
- Scope/exception: no universal application-coverage promise. The root HWND can be a foreground dialog. Independent popup/menu HWNDs outside its UIA subtree are not separately enumerated in this version.
- Approval, 2026-10-07: "good, commit, then implement on new gpt 6.1 sol high default subsession".
- Approval status: the user approved this planning context, including requirements, risks, implementation choices/defaults/limits, and execution in a new default-agent session using GPT-6.1 Sol with high reasoning. This authorizes implementation, not a claim that live verification has passed.

## Success signals

- A controlled Windows fixture exposes its known enabled buttons/edit fields as independently selectable targets.
- A selected target receives the requested physical mouse action, not an implicit UIA Invoke.
- A hung provider cannot freeze Klikety or leave unbounded helper processes.
- Missing coverage, partial results, and rejected actions have visible explanations and a grid escape route.
- Existing grid navigation, help, actions, macro data, and display behavior remain intact.

## Non-goals

- Whole-display/window enumeration, separate popup discovery, and desktop-wide UIA scans.
- OCR, screenshots, visual models, browser extensions/CDP, app-specific adapters, and remote-host agents.
- Installing/changing target-app accessibility settings or forcing browser accessibility flags.
- Automatic elevation, UIAccess manifests, UAC/lock-screen control, or privilege-boundary workarounds.
- Semantic UIA action execution, automatic text entry, reading textbox values/passwords/document content, or persistent accessibility-tree dumps.
- Continuous tree-event monitoring, live relabeling, and automatic retry loops.
- Element-aware macro storage/playback and new drag semantics.
- Fixing unrelated discrepancies in existing design notes.

### Delegated discretion and deferred choices

- Implementation discretion: use existing typed interfaces, label resolvers, logging, notification, coordinate, and test patterns. Component names in the plan are suggestions, not a closed file list.
- Approved implementation choices: opt-in rollout, Tab chord, Enter grid fallback, arrow-key paging, scan/validation budgets, and traversal/output caps in `decisions.md`, accepted through the user's whole-plan approval.
- Owner: user approved the complete planning context before execution. Changes to the three confirmed choices, a new third-party package, an in-process fallback, or broader app scope require another decision.
- Technical gate: establish that managed UIA and bundled-helper build/publish work under .NET 10 before implementing the full renderer. If not, resolve that specific blocker rather than redesigning unrelated subsystems.

## Definition of done

- Requirements have focused automated evidence; known fixture coverage and real-window behavior have live Windows evidence.
- The existing self-contained release includes the helper and runs from an extracted folder without a developer SDK.
- Directly related design notes, root index, configuration guidance, README, and coverage limitations describe the delivered implementation.
- No completed steps or evidence are claimed during planning.

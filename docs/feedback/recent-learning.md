# Recent learning

Source plan: `a5c375 klikety-maintenance-corrections`
Source commit: `4894f3d309eb6cbd62c2e9b75178159a68cc317b`

## Lessons

- Register queued indicator operations before dispatch so cancellation and owner-thread disposal can complete them without another dispatcher turn. — `src/Klikety/Overlay/ClickIndicatorLifecycle.cs`
- Keep playback resource completion independent of UI pumping; marshal guarded progress and restoration separately and serialize cancellation against CTS disposal. — `src/Klikety/Navigation/MacroHandler.cs`
- Track held synthetic inputs from accepted prefixes and retain primary diagnostics alongside one release-only cleanup result; never replay a failed action. — `src/Klikety/Services/MouseActionService.cs`
- Use archive and recreation commits to establish duplicate-plan ownership, then retain both records and restore assets with exact hash checks. — `docs/implementation-plans/021-keyboard-layout-refresh/plan.md`
- Treat recorded inter-step intervals as stable data; await owned drag completion before starting the following interval. — `docs/design-notes/macros.design.md`

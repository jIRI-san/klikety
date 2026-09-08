# Risks

<!-- For uncertain or high-impact work, the mitigation names the concrete stop/escalation condition. -->

| ID | Risk | Likelihood | Impact | Mitigation | Steps |
|----|------|------------|--------|------------|-------|
| RISK-1 | Extra WPF windows deactivate the navigation overlay (`Deactivated` → `DeactivateOverlay`) | High | High | Satellites use `WS_EX_NOACTIVATE`, `TOOLWINDOW`, `TRANSPARENT`; never `Activate()`. Show satellites before nav. Keep the existing focus-lost switching guard around host show/hide. Stop/escalate when: showing satellites dismisses the nav overlay in a two-display manual run. | 3.1, 5.2 |
| RISK-2 | CCD DevicePath empty or duplicated so numbering cannot be stable | Medium | High | Catalog returns a hard failure; do not invent names or merge duplicates. Stop/escalate when: any active display has empty or duplicate DevicePath on a machine we need to support. | 1.1, 1.2, 1.3 |
| RISK-3 | CCD active-path count does not match `EnumDisplayMonitors` count | Medium | High | Treat as identity failure; do not heuristically pair leftovers. Stop/escalate when: a standard extend topology reports mismatched counts. | 1.1, 1.3 |
| RISK-4 | Mixed-DPI without Per-Monitor V2 sizes overlays with the wrong DIP scale | High | High | Ship `app.manifest` PerMonitorV2 and size each window from its own `PresentationSource`. Stop/escalate when: overlay DIP width/height is not physical size divided by that display’s `GetDpiForMonitor` scale (tolerance 1 DIP). | 2.2, 5.2 |
| RISK-5 | Virtual desktop origin is not (0,0); primary-relative `SendInput` misses | High | High | Normalize against virtual-screen metrics, including negative origin. Stop/escalate when: a click on a display whose origin is negative lands on the wrong display. | 2.1 |

TickLab v1.13.0.55 — Instant Replay Index — Compile Fix

Build-stop fix only:
- Added the missing `using TickLab.Core.Diagnostics;` import to MainWindow.AlertsReplay.cs.
- This resolves the three compiler errors for TickLabErrorEngine, TickLabErrorContext, and TickLabErrorSeverity in the replay index warmup error-report block.
- No replay behavior, MT5 bridge behavior, history logic, trading, alerts, panels, indicators, or other features were changed.

Validation:
- Existing v1.13.0.55 source validator: 696 passed, 0 failed.
- Checked all C# references to TickLabErrorEngine/TickLabErrorContext/TickLabErrorSeverity for required namespace import.

The remaining messages about unused events and nullable references are compiler warnings, not build-stopping errors.

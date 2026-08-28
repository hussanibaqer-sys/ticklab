TickLab v1.13.0.63 — Raw Tick Chart Crosshair Compile Fix

Fixed build-stopping compiler error in src/TickLab.App/Controls/TickChartControl.cs:
- Added the missing private CrosshairPen used by DrawCrosshair().
- Raw Tick chart behavior, Alert contrast fixes, Replay, MT5 bridge and all unrelated features are unchanged.

Validation:
- validate_source_1_13_0_63.py: 731 passed, 0 failed.
- Final Windows/WPF compilation must be run in Visual Studio on Windows.

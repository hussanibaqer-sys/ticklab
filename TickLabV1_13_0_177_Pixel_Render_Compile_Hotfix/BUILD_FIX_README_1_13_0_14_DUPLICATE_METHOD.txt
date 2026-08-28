TickLab v1.13.0.14 Duplicate Method Compile Hotfix

Compiler error fixed:
Type 'CandleChartControl' already defines a member called 'FindNearestCandleIndex' with the same parameter types.

Cause:
The Step 4 synthetic-chart integration added a newer viewport-anchor implementation of FindNearestCandleIndex in CandleChartControl.cs while an older equivalent implementation remained in CandleChartControl.Drawing.cs. Because CandleChartControl is a partial class, both methods compiled into the same type and caused CS0111.

Repair:
- Kept the viewport-safe implementation in Controls/CandleChartControl.cs.
- Removed only the duplicate implementation from Controls/CandleChartControl.Drawing.cs.
- Drawing tools, synthetic chart anchoring, replay, and draggable alert lines continue to call the single shared method.
- No MT5 bridge source was changed.

Build steps:
1. Close TickLab and Visual Studio.
2. Replace the previous v1.13.0.14 source folder with this hotfix folder.
3. Delete src/TickLab.App/bin and src/TickLab.App/obj if they exist.
4. Open TickLabV1_13_0_14.sln.
5. Run Clean Solution, then Rebuild Solution.

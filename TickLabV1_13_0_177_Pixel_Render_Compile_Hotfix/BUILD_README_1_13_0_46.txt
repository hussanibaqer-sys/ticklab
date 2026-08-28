TickLab v1.13.0.46 — History, SL/TP, P&L and Multi-Chart Live Fix

1. Extract the ZIP to a normal local folder outside OneDrive/cloud sync.
2. Open TickLabV1_13_0_46.sln in Visual Studio 2022 on Windows.
3. Close TickLab if it is running.
4. Run Clean-Restore-Build.cmd, or choose Build > Rebuild Solution (Release).
5. Launch TickLab.App and test the checklist in FIRST_TEST_CHECKLIST_1_13_0_46.txt.

This package does not modify the approved MT5 bridge or FileBridge source files.
The generation environment has no Windows WPF compiler, so the Visual Studio
Release rebuild is the final compiler and display test.

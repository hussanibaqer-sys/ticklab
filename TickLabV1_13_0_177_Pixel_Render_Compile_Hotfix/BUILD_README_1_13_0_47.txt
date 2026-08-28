TickLab v1.13.0.47 — Alerts, Favourites, Entry Drag and Shared-Rail Fix

1. Extract the ZIP to a normal local folder outside OneDrive/cloud sync.
2. Open TickLabV1_13_0_47.sln in Visual Studio 2022 on Windows.
3. Close every running TickLab instance.
4. Run Clean-Restore-Build.cmd, or choose Build > Rebuild Solution using Release.
5. Launch TickLab.App and follow FIRST_TEST_CHECKLIST_1_13_0_47.txt.

The package does not modify approved MT5 bridge or FileBridge source files.
The generation environment does not contain the Windows WPF/.NET compiler, so a
Visual Studio 2022 Release rebuild remains the final compiler and visual test.

TickLab v1.13.0.5 build instructions

1. Close every older TickLab solution and running TickLab window.
2. Extract the complete package to C:\TickLab\TickLabV1_13_0_5 (not OneDrive).
3. Open TickLabV1_13_0_5.sln.
4. Select Release and Any CPU.
5. Build -> Clean Solution.
6. Build -> Rebuild Solution.
7. Do not copy individual .cs or .xaml files from v1.13.0.4 or any broken folder.

This package introduces MainWindow layout changes and a new DetachedChartWindow XAML/code-behind pair. Both files must remain together in the project.

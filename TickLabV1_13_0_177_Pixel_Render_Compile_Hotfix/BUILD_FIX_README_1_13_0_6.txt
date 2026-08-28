TickLab v1.13.0.6 build instructions

1. Close every older TickLab solution and running TickLab window.
2. Extract the complete package to C:\TickLab\TickLabV1_13_0_6 outside OneDrive.
3. Open TickLabV1_13_0_6.sln.
4. Select Release and Any CPU.
5. Build -> Clean Solution.
6. Build -> Rebuild Solution.
7. Keep MainWindow.Workspaces.cs, WorkspaceSurfaceControl.cs, ChartPaneControl.cs, DetachedWorkspaceWindow.xaml/.cs, DetachedChartWindow.xaml/.cs and WorkspaceDecisionDialog.cs together in the project.
8. Do not copy individual files from v1.13.0.5 into this folder.

If Visual Studio shows stale designer errors:
- close Visual Studio
- delete the src\TickLab.App\bin and src\TickLab.App\obj folders
- reopen TickLabV1_13_0_6.sln
- Restore NuGet Packages, then Rebuild Solution

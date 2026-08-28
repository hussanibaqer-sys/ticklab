TickLab v1.13.0.15 build instructions

1. Close TickLab and Visual Studio.
2. Extract the package outside OneDrive, preferably C:\TickLab\TickLabV1_13_0_15.
3. Delete src\TickLab.App\bin and src\TickLab.App\obj if they exist.
4. Open TickLabV1_13_0_15.sln in Visual Studio 2022.
5. Select Build > Clean Solution.
6. Select Build > Rebuild Solution.

If a DLL is locked, end TickLab.exe, dotnet.exe, MSBuild.exe and VBCSCompiler.exe in Task Manager, then delete bin and obj again.

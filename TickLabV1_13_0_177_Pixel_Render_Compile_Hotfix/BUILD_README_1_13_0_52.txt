TickLab v1.13.0.52 build
========================

Recommended location:
C:\TickLab\TickLabV1_13_0_52\

Do not run an older TickLab instance while rebuilding.
Run Clean-Restore-Build.cmd, or open TickLabV1_13_0_52.sln in Visual Studio 2022 and choose Release > Rebuild Solution.

If TickLab.dll is locked:
1. Close TickLab and Visual Studio.
2. End TickLab.exe, dotnet.exe, MSBuild.exe and VBCSCompiler.exe in Task Manager.
3. Delete src\TickLab.App\bin and src\TickLab.App\obj.
4. Rebuild.

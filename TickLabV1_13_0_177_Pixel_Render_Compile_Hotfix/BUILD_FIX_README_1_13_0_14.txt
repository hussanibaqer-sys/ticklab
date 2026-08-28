TickLab v1.13.0.14 build instructions

If Visual Studio reports that TickLab.dll is in use:
1. Close TickLab and Visual Studio.
2. End TickLab.exe, dotnet.exe, MSBuild.exe and VBCSCompiler.exe in Task Manager.
3. Delete src\TickLab.App\bin and src\TickLab.App\obj.
4. Reopen TickLabV1_13_0_14.sln and rebuild.

The source package was statically validated on Linux. A Windows Visual Studio
build remains the authoritative WPF compiler and runtime test.

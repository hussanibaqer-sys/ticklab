TickLab v1.13.0.44 build instructions

1. Extract the complete folder to a local non-cloud path, for example C:\TickLab\v1.13.0.44.
2. Open TickLabV1_13_0_44.sln in Visual Studio 2022.
3. Select Release and x64/Any CPU as appropriate for the existing solution.
4. Use Build > Rebuild Solution.

Alternative:
- Double-click Clean-Restore-Build.cmd after confirming the .NET 8 Windows desktop SDK is installed.

Important:
- Delete old bin and obj folders if Visual Studio reports stale generated files.
- The package was statically validated in Linux, but Linux does not provide the Windows WPF compiler used for the final build.

TickLab v1.13.0.39 — Clean Windows build

1. Extract the ZIP to:
   C:\TickLab\TickLabV1_13_0_39_Restart_Step7

2. Close TickLab and Visual Studio.

3. Press Win + R and paste:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_39_Restart_Step7" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"

4. Open:
   TickLabV1_13_0_39.sln

5. In Visual Studio 2022:
   - Restore NuGet packages.
   - Build > Clean Solution.
   - Build > Rebuild Solution.
   - Run the WPF application.

Requirements
- Windows 10 or Windows 11.
- Visual Studio 2022 with .NET desktop development.
- .NET 8 SDK.

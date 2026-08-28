TickLab v1.13.0.38 — Clean Windows build

1. Extract the ZIP to:
   C:\TickLab\TickLabV1_13_0_38_Restart_Step6A

2. Close TickLab and Visual Studio.

3. Press Win + R and paste:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_38_Restart_Step6A" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"

4. Open:
   TickLabV1_13_0_38.sln

5. In Visual Studio 2022:
   - Restore NuGet packages.
   - Build > Clean Solution.
   - Build > Rebuild Solution.
   - Run the WPF application.

Requirements
- Windows 10 or Windows 11.
- Visual Studio 2022 with .NET desktop development.
- .NET 8 SDK.

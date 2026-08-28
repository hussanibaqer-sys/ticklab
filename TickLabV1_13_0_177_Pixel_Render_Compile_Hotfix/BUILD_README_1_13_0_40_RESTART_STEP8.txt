TickLab v1.13.0.40 — Clean Windows build

1. Extract to:
   C:\TickLab\TickLabV1_13_0_40_Restart_Step8

2. Press Win + R and paste:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_40_Restart_Step8" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"

3. Open:
   TickLabV1_13_0_40.sln

4. In Visual Studio 2022:
   Restore NuGet packages, then Rebuild Solution.

TickLab v1.13.0.41 — Clean Windows build

1. Extract the package to:
   C:\TickLab\TickLabV1_13_0_41_Restart_Step9

2. Press Win + R and paste:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_41_Restart_Step9" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"

3. Open:
   TickLabV1_13_0_41.sln

4. In Visual Studio 2022:
   Build > Rebuild Solution

5. Test the checklist in FIRST_TEST_CHECKLIST_1_13_0_41_RESTART_STEP9.txt.

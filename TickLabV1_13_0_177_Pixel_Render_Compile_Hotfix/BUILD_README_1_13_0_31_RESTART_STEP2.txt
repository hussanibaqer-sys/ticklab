TickLab v1.13.0.31 Restart Step 2 — Windows Build

1. Extract to C:\TickLab\TickLabV1_13_0_31_Restart_Step2
2. Press Win + R and run:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_31_Restart_Step2" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"
3. Open TickLabV1_13_0_31.sln in Visual Studio 2022.
4. Rebuild Solution.
5. Follow FIRST_TEST_CHECKLIST_1_13_0_31_RESTART_STEP2.txt.

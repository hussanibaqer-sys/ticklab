TickLab v1.13.0.32 Restart Step 3 — Windows Build

1. Extract to C:\TickLab\TickLabV1_13_0_32_Restart_Step3
2. Press Win + R and run:
   cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_32_Restart_Step3" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"
3. Open TickLabV1_13_0_32.sln in Visual Studio 2022.
4. Restore and rebuild with .NET 8.
5. Follow FIRST_TEST_CHECKLIST_1_13_0_32_RESTART_STEP3.txt.

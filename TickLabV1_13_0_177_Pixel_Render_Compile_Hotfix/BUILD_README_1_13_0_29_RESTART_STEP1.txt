TickLab v1.13.0.29 Restart Step 1 — Windows build

Recommended folder:
C:\TickLab\TickLabV1_13_0_29_Restart_Step1

Win + R clean command:
cmd /c for /d /r "C:\TickLab\TickLabV1_13_0_29_Restart_Step1" %d in (bin,obj) do @if exist "%d" rd /s /q "%d"

Then open TickLabV1_13_0_29.sln, rebuild, and follow FIRST_TEST_CHECKLIST_1_13_0_29_RESTART_STEP1.txt.

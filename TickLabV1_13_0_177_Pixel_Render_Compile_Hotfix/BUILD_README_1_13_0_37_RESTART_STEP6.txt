TickLab v1.13.0.37 — Restart Step 6 build steps

1. Close TickLab and Visual Studio.
2. Extract outside OneDrive, preferably:
   C:\TickLab\TickLabV1_13_0_37_Restart_Step6
3. Delete every bin and obj folder.
4. Open TickLabV1_13_0_37.sln in Visual Studio 2022.
5. Select Build > Clean Solution.
6. Select Build > Rebuild Solution.
7. Launch TickLab and follow FIRST_TEST_CHECKLIST_1_13_0_37_RESTART_STEP6.txt.

Important
- Demo Trading is simulation-only. It contains no real MT5 order API.
- This source package was statically validated in Linux, where the .NET SDK and Windows WPF runtime are unavailable. A Windows Visual Studio 2022 / .NET 8 build is required.

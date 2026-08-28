TickLab v1.13.0.28 — Windows Build Steps

1. Extract to a local non-cloud folder, for example:
   C:\TickLab\TickLabV1_13_0_28

2. Do not build inside the ZIP, OneDrive, Dropbox or a network drive.

3. Open TickLabV1_13_0_28.sln in Visual Studio 2022.

4. Confirm Visual Studio Installer includes:
   - .NET desktop development workload
   - .NET 8 SDK
   - Windows 10/11 SDK

5. Close every older TickLab process.

6. Delete every bin and obj folder, then:
   Build > Clean Solution
   Build > Rebuild Solution

7. Start with F5 and follow FIRST_TEST_CHECKLIST_1_13_0_28.txt.

DO NOT RECOMPILE OR REPLACE THE MT5 BRIDGE FOR THIS TEST.
The protected MT5 source files are unchanged.

If the build fails, provide the exact Visual Studio error code, file, line and complete message.

TickLab v1.13.0.27 — Windows Build Steps

1. Extract the ZIP to a normal local folder, for example:
   C:\TickLab\TickLabV1_13_0_27

2. Do not build from OneDrive, Dropbox, a network drive or inside the ZIP.

3. Open:
   TickLabV1_13_0_27.sln

4. In Visual Studio Installer, confirm these are installed:
   - Visual Studio 2022
   - .NET desktop development workload
   - .NET 8 SDK
   - Windows 10/11 SDK

5. Close TickLab if an older version is running.

6. In Visual Studio:
   - Build > Clean Solution
   - Build > Rebuild Solution

7. If old bin/obj files cause an error:
   - Close Visual Studio.
   - Run Clean-Restore-Build.cmd from this folder.
   - Reopen TickLabV1_13_0_27.sln.
   - Rebuild.

8. Start with F5.

9. Follow FIRST_TEST_CHECKLIST_1_13_0_27.txt before replacing your previous working folder.

DO NOT RECOMPILE OR REPLACE THE MT5 BRIDGES FOR THIS TEST
The MT5 source files are unchanged from v1.13.0.26. Keep your currently working bridge attached and test the desktop app first.

IF THE BUILD FAILS
Copy the complete Visual Studio Error List entry, including:
- error code
- file name
- line number
- full message
Do not delete the stable v1.13.0.26 folder.

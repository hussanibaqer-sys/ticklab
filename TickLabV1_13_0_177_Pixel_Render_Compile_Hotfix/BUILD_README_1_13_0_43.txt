TickLab v1.13.0.43 — Build Instructions

1. Extract the ZIP to a normal local folder, for example C:\TickLab\TickLabV1_13_0_43.
2. Do not build from OneDrive, Dropbox, a network drive or another cloud-synced folder.
3. Open TickLabV1_13_0_43.sln in Visual Studio 2022.
4. Select Release and Any CPU.
5. In Visual Studio choose Build > Clean Solution.
6. Choose Build > Rebuild Solution.
7. Start TickLab.App.

Alternative:
- Run Clean-Restore-Build.cmd from the extracted project folder.

Before testing this version, close every older TickLab process. This build keeps the existing demo-account persistence location, so saved demo positions and history should load automatically.

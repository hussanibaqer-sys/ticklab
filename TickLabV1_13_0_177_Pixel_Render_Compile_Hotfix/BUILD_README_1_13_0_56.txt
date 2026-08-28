TickLab v1.13.0.56 — Replay Source Recovery

Build on Windows / Visual Studio 2022:
1. Close TickLab and stop Visual Studio debugging.
2. Run Clean-Restore-Build.cmd.
3. Or open TickLabV1_13_0_56.sln and Rebuild Release.

If you want to clear build outputs manually first, press Win+R and run:
cmd /c "for /d /r %i in (bin,obj) do @if exist \"%i\" rd /s /q \"%i\""

This release changes Replay reliability only. MT5 bridge source files are unchanged.

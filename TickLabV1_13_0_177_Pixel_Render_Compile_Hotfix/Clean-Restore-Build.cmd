@echo off
setlocal
cd /d "%~dp0"
dotnet clean TickLabV1_13_0_133.sln
dotnet restore TickLabV1_13_0_133.sln
if errorlevel 1 exit /b %errorlevel%
dotnet build TickLabV1_13_0_133.sln -c Release
endlocal

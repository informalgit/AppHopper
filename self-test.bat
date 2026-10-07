@echo off
rem Runs the regression suite that lives inside AppHopper.exe. There is no
rem separate test project: every check is compiled in and reachable through
rem the --self-test switch, so this script only builds and calls it.
cd /d "%~dp0"
setlocal
set "BUILD=%TEMP%\AppHopper-selftest-%RANDOM%-%RANDOM%.exe"
rem No app.manifest here on purpose: the suite needs no elevation, so it can
rem run next to the always-elevated installed instance without a UAC prompt.
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:exe -platform:anycpu -optimize+ -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -out:"%BUILD%" AppHopper.cs
if errorlevel 1 goto cleanup
"%BUILD%" --self-test
set "RESULT=%errorlevel%"

:cleanup
del /q "%BUILD%" >nul 2>&1
exit /b %RESULT%
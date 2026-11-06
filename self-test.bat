@echo off
rem Runs the regression suites that live inside AppHopper.exe. There is no
rem separate test project: every check is compiled in and reachable through
rem --self-test plus two opt-in suites; this script just builds and calls them.
cd /d "%~dp0"
setlocal
set "BUILD=%TEMP%\AppHopper-selftest-%RANDOM%-%RANDOM%.exe"
rem No app.manifest here on purpose: the suite needs no elevation, so it can
rem run next to the always-elevated installed instance without a UAC prompt.
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:exe -platform:anycpu -optimize+ -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:Microsoft.CSharp.dll -out:"%BUILD%" AppHopper.cs
if errorlevel 1 (
  set "RESULT=%errorlevel%"
  goto cleanup
)
"%BUILD%" --self-test
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" goto cleanup
rem Optional suites; pass --hook-health and/or --icon-latency (any order).
rem --hook-health installs/removes real hooks and sends tagged F24 ups, so it
rem needs an unlocked interactive desktop. Neither suite needs elevation.
if "%~1"=="" goto cleanup
for %%A in (%*) do call :run_optional "%%~A"
goto cleanup

:cleanup
del /q "%BUILD%" >nul 2>&1
exit /b %RESULT%

:run_optional
if /i "%~1"=="--hook-health" goto run_hook
if /i "%~1"=="--icon-latency" goto run_icon
exit /b 0
:run_hook
"%BUILD%" --hook-health-test
if errorlevel 1 set "RESULT=1"
exit /b 0
:run_icon
"%BUILD%" --icon-latency-test
if errorlevel 1 set "RESULT=1"
exit /b 0

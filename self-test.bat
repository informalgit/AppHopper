@echo off
rem Compile the current source and run isolated behavioral regressions.
cd /d "%~dp0"
setlocal
set "TEST_DIR=%TEMP%\AppHopper-tests-%RANDOM%-%RANDOM%"
mkdir "%TEST_DIR%" || exit /b 2
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
"%CSC%" -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -out:"%TEST_DIR%\AppHopper.exe" AppHopper.cs
if errorlevel 1 goto compile_failed
"%CSC%" -nologo -target:exe -r:System.dll -r:System.Core.dll -out:"%TEST_DIR%\RegressionTests.exe" tests\RegressionTests.cs
if errorlevel 1 goto compile_failed
"%TEST_DIR%\RegressionTests.exe" "%TEST_DIR%\AppHopper.exe"
set "RESULT=%errorlevel%"
goto cleanup

:compile_failed
set "RESULT=2"

:cleanup
rmdir /s /q "%TEST_DIR%"
exit /b %RESULT%

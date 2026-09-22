@echo off
rem Build AppHopper.exe with the C# compiler that ships with Windows.
rem Kills any running instance first so the output file is not locked.
cd /d "%~dp0"

taskkill /IM AppHopper.exe /F >nul 2>&1
if exist AppHopper.exe del /q AppHopper.exe
if exist AppHopper.exe (
  echo [FAIL] Could not remove the previous AppHopper.exe. Exit the running instance and retry.
  pause
  exit /b 1
)

"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -out:AppHopper.exe AppHopper.cs

if %errorlevel%==0 (
  echo.
  echo [OK] AppHopper.exe built successfully.
) else (
  echo.
  echo [FAIL] Build failed with errorlevel %errorlevel%.
)
echo.
pause

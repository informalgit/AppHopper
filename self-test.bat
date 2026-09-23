@echo off
rem Run AppHopper's pure-logic regression checks.
cd /d "%~dp0"

if not exist AppHopper.exe (
  echo [FAIL] AppHopper.exe is missing. Run build.bat first.
  exit /b 2
)

AppHopper.exe --self-test
if %errorlevel%==0 (
  echo [OK] AppHopper self-tests passed.
  exit /b 0
) else (
  echo [FAIL] AppHopper self-tests failed with errorlevel %errorlevel%.
  exit /b %errorlevel%
)

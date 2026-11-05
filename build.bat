@echo off
rem Build AppHopper.exe with the C# compiler that ships with Windows.
rem Compile a same-directory candidate first; keep the current binary on errors.
setlocal
rem Prefer the system tools over anything earlier on PATH (a Git/Cygwin shell
rem can put its own find/tasklist ahead of System32 when it launches this file).
set "PATH=%SystemRoot%\System32;%SystemRoot%;%PATH%"
cd /d "%~dp0"
if errorlevel 1 (
  echo [FAIL] Could not enter the build directory.
  set "RESULT=1"
  goto finish
)

set "RESULT=1"
set "TARGET=%~dp0AppHopper.exe"
if exist "%TARGET%\" goto target_is_directory

rem Compile the candidate first. Nothing holds the target file yet - the
rem candidate has its own name - so a failed compile can leave both the
rem installed binary and any running instance exactly as they were.
:choose_build
set "BUILD=%~dp0AppHopper.build-%RANDOM%-%RANDOM%.exe"
if exist "%BUILD%" goto choose_build

"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -platform:anycpu -optimize+ -win32manifest:app.manifest -r:System.dll -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:Microsoft.CSharp.dll -out:"%BUILD%" AppHopper.cs
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" goto compile_failed
if not exist "%BUILD%" (
  set "RESULT=1"
  echo [FAIL] The compiler succeeded but did not create a candidate executable.
  goto compile_failed
)

rem The candidate compiled, so promotion is next and the target file must be
rem free. An elevated instance locks it and refuses taskkill from this shell,
rem so fall back to ending the scheduled task that launched it - that is
rem allowed at the current integrity level.
rem Test seam: the build-regression fixture defines this variable, so a fixture
rem run can never touch a live switcher.
if defined APPHOPPER_BUILD_SKIP_STOP goto promote_target
call :is_running
if "%RUNNING%"=="0" goto promote_target
echo [INFO] Stopping the running AppHopper instance...
taskkill /IM AppHopper.exe /F >nul 2>&1
call :is_running
if "%RUNNING%"=="0" goto stopped_ok
rem Only the tasks this product registers start with "\AppHopper-". Matching
rem the whole CSV line would also end unrelated tasks that merely mention the
rem name, so filter on the task path itself.
for /f "tokens=1 delims=," %%T in ('schtasks /query /fo csv /nh 2^>nul ^| findstr /I AppHopper') do call :end_task "%%~T"
rem Ending a task is asynchronous: the process can take a moment to exit.
set "WAIT=0"
:wait_stop
call :is_running
if "%RUNNING%"=="0" goto stopped_ok
set /a WAIT+=1
if %WAIT% GEQ 20 goto still_running
ping -n 2 127.0.0.1 >nul
goto wait_stop
:still_running
echo [FAIL] AppHopper is still running and this shell cannot stop it.
echo [INFO] Quit it from the tray, or run build.bat as administrator, then retry.
echo [INFO] The candidate was discarded and the existing AppHopper.exe was left in place.
set "RESULT=1"
goto cleanup_candidate
:stopped_ok
echo [INFO] AppHopper stopped; start it again when you are ready to test.

:promote_target
set "HAD_TARGET=0"
if exist "%TARGET%" set "HAD_TARGET=1"
if "%HAD_TARGET%"=="1" goto backup_old

goto promote_candidate

:backup_old
:choose_backup
set "BACKUP=%~dp0AppHopper.previous-%RANDOM%-%RANDOM%.exe"
if exist "%BACKUP%" goto choose_backup
move /y "%TARGET%" "%BACKUP%" >nul
if errorlevel 1 goto old_move_failed
if not exist "%BACKUP%" goto old_move_failed
if exist "%TARGET%" goto old_move_failed

goto promote_candidate

:promote_candidate
ren "%BUILD%" "AppHopper.exe" >nul
set "PROMOTE_RESULT=%ERRORLEVEL%"
if not "%PROMOTE_RESULT%"=="0" goto promotion_failed
if not exist "%TARGET%" goto promotion_failed
if exist "%BUILD%" goto promotion_failed

if "%HAD_TARGET%"=="1" if exist "%BACKUP%" del /q "%BACKUP%" >nul 2>&1
if "%HAD_TARGET%"=="1" if exist "%BACKUP%" echo [WARN] New build installed; previous-binary backup remains at "%BACKUP%".
echo.
echo [OK] AppHopper.exe built successfully.
echo [INFO] Start AppHopper when you are ready to test the new build.
set "RESULT=0"
goto finish

:compile_failed
if exist "%BUILD%" del /q "%BUILD%" >nul 2>&1
echo.
echo [FAIL] Build failed with errorlevel %RESULT%.
echo [INFO] The existing AppHopper.exe and any running instance were left untouched.
if exist "%BUILD%" echo [WARN] Remove the leftover candidate after closing any process using it: "%BUILD%".
goto finish

:target_is_directory
echo [FAIL] The AppHopper.exe path is a directory. Move it aside before building.
goto finish

:old_move_failed
set "RESULT=1"
if exist "%TARGET%" goto old_still_at_target
if not exist "%BACKUP%" goto old_binary_missing
move /y "%BACKUP%" "%TARGET%" >nul
if errorlevel 1 goto old_recovery_failed
if not exist "%TARGET%" goto old_recovery_failed
echo.
echo [FAIL] Could not stage the existing AppHopper.exe.
echo [INFO] The previous executable was restored; close AppHopper and retry.
goto cleanup_candidate

:old_still_at_target
echo.
echo [FAIL] Could not move the existing AppHopper.exe. Close AppHopper and retry.
echo [INFO] The previous executable remains at "%TARGET%"; the candidate was not installed.
if exist "%BACKUP%" echo [RECOVERY] An additional recovery copy is at "%BACKUP%".
goto cleanup_candidate

:old_binary_missing
echo.
echo [FAIL] Could not stage the existing AppHopper.exe, and no recovery copy is available.
echo [INFO] Check directory permissions and retry; the candidate was not installed.
goto cleanup_candidate

:old_recovery_failed
echo.
echo [FAIL] Could not stage or restore the existing AppHopper.exe.
echo [RECOVERY] The previous executable is preserved at "%BACKUP%".
echo [INFO] Close AppHopper, then move that file back to "%TARGET%".
goto cleanup_candidate

:promotion_failed
set "RESULT=%PROMOTE_RESULT%"
if "%RESULT%"=="0" set "RESULT=1"
if "%HAD_TARGET%"=="1" if exist "%BACKUP%" goto restore_old
echo.
echo [FAIL] Could not install the compiled candidate (errorlevel %RESULT%).
if "%HAD_TARGET%"=="0" echo [INFO] No previous executable existed; no candidate was installed.
goto cleanup_candidate

:restore_old
if exist "%TARGET%" goto rollback_copy_preserved
goto try_restore_old

:try_restore_old
move /y "%BACKUP%" "%TARGET%" >nul
if errorlevel 1 goto rollback_copy_preserved
if not exist "%TARGET%" goto rollback_copy_preserved
echo.
echo [FAIL] Could not install the compiled candidate (errorlevel %RESULT%).
echo [INFO] The previous AppHopper.exe was restored.
goto cleanup_candidate

:rollback_copy_preserved
echo.
echo [FAIL] Candidate promotion failed and automatic rollback could not restore the target.
echo [RECOVERY] The previous executable is preserved at "%BACKUP%".
echo [INFO] Close AppHopper, then move that file back to "%TARGET%".
goto cleanup_candidate

:cleanup_candidate
if exist "%BUILD%" del /q "%BUILD%" >nul 2>&1
if exist "%BUILD%" echo [WARN] Temporary candidate remains at "%BUILD%".

:finish
pause
exit /b %RESULT%

:is_running
set "RUNNING=0"
"%SystemRoot%\System32\tasklist.exe" /FI "IMAGENAME eq AppHopper.exe" 2>nul | "%SystemRoot%\System32\find.exe" /I "AppHopper.exe" >nul
if not errorlevel 1 set "RUNNING=1"
exit /b 0

:end_task
set "TASK=%~1"
if /I not "%TASK:~0,11%"=="\AppHopper-" exit /b 0
schtasks /end /tn "%TASK%" >nul 2>&1
exit /b 0

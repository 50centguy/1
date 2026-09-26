@echo off
rem Worker-hand narrative slice: build assets/scene, apply polish, run EditMode + PlayMode tests, capture screenshots. Logs: unity_logs\
rem Close the Unity Editor for this project before running (batchmode needs the project lock).
rem Arguments (any order):
rem   rebuild   overwrite the generated slice assets (prefab, case, shift, scene) - also re-applies the polish
rem   repolish  rebuild only the polish objects in the narrative scene (framing, feedback, panels, materials, lighting)
rem   nopause   do not pause at the end (for scripted runs)
rem Stops at the first failing step and exits non-zero; old result XML files are deleted before each step.
setlocal
chcp 65001 >nul
set "HERE=%~dp0"
for %%I in ("%HERE%..\..") do set "PROJECT=%%~fI"
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe"
set "LOGS=%HERE%unity_logs"
set "RC=0"
if not exist "%LOGS%" mkdir "%LOGS%"
set "METHOD=BuildFromCommandLine"
set "POLISH=ApplyFromCommandLine"
set "NOPAUSE="
for %%A in (%*) do (
    if /I "%%~A"=="rebuild" set "METHOD=RebuildFromCommandLine"
    if /I "%%~A"=="repolish" set "POLISH=ReapplyFromCommandLine"
    if /I "%%~A"=="nopause" set "NOPAUSE=1"
)
if not exist "%UNITY%" (
    echo Unity not found: %UNITY%
    set "RC=9"
    goto :done
)

echo [1/5] Build narrative slice (%METHOD%)...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod BorderRepair.EditorTools.NarrativeSliceBuilder.%METHOD% -logFile "%LOGS%\1_build.log"
set "RC=%ERRORLEVEL%"
echo       exit %RC%
if not "%RC%"=="0" goto :failed

echo [2/5] Apply worker-hand polish (%POLISH%)...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod BorderRepair.EditorTools.WorkerHandPolishBuilder.%POLISH% -logFile "%LOGS%\1b_polish.log"
set "RC=%ERRORLEVEL%"
echo       exit %RC%
if not "%RC%"=="0" goto :failed

echo [3/5] EditMode tests...
call :runtests EditMode "" 2_editmode
if not "%RC%"=="0" goto :failed

echo [4/5] PlayMode tests...
call :runtests PlayMode "" 3_playmode
if not "%RC%"=="0" goto :failed

echo [5/5] In-game screenshots (batchmode capture, not a play session)...
if exist "%PROJECT%\Docs\Narrative\polish_batchmode_metrics.txt" del /q "%PROJECT%\Docs\Narrative\polish_batchmode_metrics.txt"
call :runtests PlayMode "BorderRepair.Tests.WorkerHandCaptureTests" 4_capture
if not "%RC%"=="0" goto :failed
if not exist "%PROJECT%\Docs\Narrative\polish_batchmode_metrics.txt" (
    echo       polish_batchmode_metrics.txt was not written
    set "RC=7"
    goto :failed
)

echo All steps passed. Logs in %LOGS%
goto :done

:failed
echo FAILED (exit %RC%). See "%LOGS%".

:done
if not defined NOPAUSE pause
exit /b %RC%

rem ---- :runtests <platform> <filter or ""> <name> : sets RC; a missing/stale XML or any failed/zero-passed run is a failure
:runtests
set "XML=%LOGS%\%~3.xml"
if exist "%XML%" del /q "%XML%"
if exist "%XML%" (
    echo       could not delete old %XML%
    set "RC=8"
    exit /b
)
if "%~2"=="" (
    start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" -runTests -testPlatform %~1 -testResults "%XML%" -logFile "%LOGS%\%~3.log"
) else (
    start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" -runTests -testPlatform %~1 -testFilter "%~2" -testResults "%XML%" -logFile "%LOGS%\%~3.log"
)
set "RC=%ERRORLEVEL%"
echo       unity exit %RC%
if not "%RC%"=="0" exit /b
if not exist "%XML%" (
    echo       no result XML was written
    set "RC=6"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$x = New-Object xml; $x.Load('%XML%'); $r = $x.'test-run'; Write-Host ('      result=' + $r.result + ' total=' + $r.total + ' passed=' + $r.passed + ' failed=' + $r.failed + ' skipped=' + $r.skipped); if ($r.result -notlike 'Passed*' -or [int]$r.failed -gt 0 -or [int]$r.passed -lt 1) { exit 5 } else { exit 0 }"
set "RC=%ERRORLEVEL%"
exit /b

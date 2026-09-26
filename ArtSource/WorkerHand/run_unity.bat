@echo off
rem Worker hand v2: build v2 prefab / tool rigs / scene hookup from the Blender export (compiles scripts first),
rem run EditMode + PlayMode tests, then capture the v2 tool-animation screenshots. Logs: unity_logs\
rem Run run_blender.bat --export first if the Blender source changed. Close the Unity Editor for this project first.
rem Stops at the first failing step and exits non-zero; old result XML files are deleted before each step.
rem Pass "nopause" to skip the final pause. Screenshots are batchmode captures, not a play session.
setlocal
chcp 65001 >nul
set "HERE=%~dp0"
for %%I in ("%HERE%..\..") do set "PROJECT=%%~fI"
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe"
set "LOGS=%HERE%unity_logs"
set "RC=0"
if not exist "%LOGS%" mkdir "%LOGS%"
if not exist "%UNITY%" (
    echo Unity not found: %UNITY%
    set "RC=9"
    goto :done
)

echo [1/4] Build worker hand v2 (compile + import + prefab + rigs + scene)...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod BorderRepair.EditorTools.WorkerHandV2AssetBuilder.BuildFromCommandLine -logFile "%LOGS%\1_build_v2.log"
set "RC=%ERRORLEVEL%"
echo       exit %RC%
if not "%RC%"=="0" goto :failed

echo [2/4] EditMode tests...
call :runtests EditMode "" 2_editmode
if not "%RC%"=="0" goto :failed

echo [3/4] PlayMode tests...
call :runtests PlayMode "" 3_playmode
if not "%RC%"=="0" goto :failed

echo [4/4] v2 tool-animation screenshots (batchmode capture, not a play session)...
if exist "%PROJECT%\Docs\Narrative\v2_toolanim_batchmode_metrics.txt" del /q "%PROJECT%\Docs\Narrative\v2_toolanim_batchmode_metrics.txt"
call :runtests PlayMode "BorderRepair.Tests.WorkerHandV2CaptureTests" 4_capture_v2
if not "%RC%"=="0" goto :failed
if not exist "%PROJECT%\Docs\Narrative\v2_toolanim_batchmode_metrics.txt" (
    echo       v2_toolanim_batchmode_metrics.txt was not written
    set "RC=7"
    goto :failed
)

echo All steps passed. Logs in %LOGS%
goto :done

:failed
echo FAILED (exit %RC%). See "%LOGS%".

:done
if /i not "%~1"=="nopause" pause
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

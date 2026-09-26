@echo off
rem Import NavBeacon into Unity, run tests, capture in-game screenshots. Logs: unity_logs\
rem Close the Unity Editor for this project before running (batchmode needs the project lock).
chcp 65001 >nul
set "HERE=%~dp0"
for %%I in ("%HERE%..\..") do set "PROJECT=%%~fI"
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe"
set "LOGS=%HERE%unity_logs"
if not exist "%LOGS%" mkdir "%LOGS%"

echo [1/4] Import NavBeacon and build final prefab...
start "" /wait "%UNITY%" -batchmode -quit -projectPath "%PROJECT%" -executeMethod BorderRepair.EditorTools.NavBeaconAssetBuilder.BuildFromCommandLine -logFile "%LOGS%\1_import.log"
set "RC=%ERRORLEVEL%"
echo       exit %RC%
if not "%RC%"=="0" goto :fail

echo [2/4] EditMode tests...
start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" -runTests -testPlatform EditMode -testResults "%LOGS%\2_editmode.xml" -logFile "%LOGS%\2_editmode.log"
echo       exit %ERRORLEVEL%

echo [3/4] PlayMode tests...
start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" -runTests -testPlatform PlayMode -testResults "%LOGS%\3_playmode.xml" -logFile "%LOGS%\3_playmode.log"
echo       exit %ERRORLEVEL%

echo [4/4] In-game screenshots...
start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" -runTests -testPlatform PlayMode -testFilter "BorderRepair.Tests.NavBeaconCaptureTests" -testResults "%LOGS%\4_capture.xml" -logFile "%LOGS%\4_capture.log"
echo       exit %ERRORLEVEL%

echo Done. Logs in %LOGS%
pause
exit /b 0

:fail
echo Import step failed, see %LOGS%\1_import.log
pause
exit /b 1

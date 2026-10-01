@echo off
rem Import the workbench area into Unity, build prefab + test scene, run EditMode / PlayMode tests, then the GUI play capture.
chcp 65001 >nul
set "HERE=%~dp0"
for %%I in ("%HERE%..\..") do set "PROJ=%%~fI"
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe"
set "LOGS=%PROJ%\Logs\wb"
if not exist "%LOGS%" mkdir "%LOGS%"
echo [1/4] Build test scene
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\build.log" -executeMethod WorkbenchArea.EditorTools.WorkbenchAreaBuilder.BuildAll
echo Exit code: %ERRORLEVEL%
echo [2/4] EditMode tests
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\edit.log" -runTests -testPlatform EditMode -assemblyNames WorkbenchArea.Tests.EditMode -testResults "%LOGS%\edit.xml"
echo Exit code: %ERRORLEVEL%
echo [3/4] PlayMode tests
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\play.log" -runTests -testPlatform PlayMode -assemblyNames WorkbenchArea.Tests.PlayMode -testResults "%LOGS%\play.xml"
echo Exit code: %ERRORLEVEL%
echo [4/4] GUI play capture (opens the editor window; keep it in front)
"%UNITY%" -projectPath "%PROJ%" -logFile "%LOGS%\capture.log" -executeMethod WorkbenchArea.EditorTools.WorkbenchAreaPlayCapture.Begin
echo Exit code: %ERRORLEVEL%
echo Results: %HERE%Reports\Unity\
pause

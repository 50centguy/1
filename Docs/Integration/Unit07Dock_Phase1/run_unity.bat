@echo off
rem UNIT 07 维修座第一阶段接入：构建 -> EditMode 测试 -> PlayMode 测试 -> 带界面的 Play 模式采集（截图、渲染统计）。
rem 任一步失败即停止并返回非零退出码。日志在 Logs\unit07\。参数 nopause：结尾不暂停；nocapture：跳过带界面的采集。
chcp 65001 >nul
setlocal
set "PROJ=%~dp0..\..\.."
set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe"
set "LOGS=%PROJ%\Logs\unit07"
if not exist "%LOGS%" mkdir "%LOGS%"

echo [1/4] Build (import dock FBX, materials, prefabs, test scene)
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\build.log" -executeMethod BorderRepair.EditorTools.Unit07DockBuilder.BuildAll
if errorlevel 1 goto :fail

echo [2/4] EditMode tests (Unit07DockEditModeTests)
if exist "%LOGS%\editmode.xml" del "%LOGS%\editmode.xml"
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\editmode.log" -runTests -testPlatform EditMode -testFilter BorderRepair.Tests.Unit07DockEditModeTests -testResults "%LOGS%\editmode.xml"
if errorlevel 1 goto :fail

echo [3/4] PlayMode tests (Unit07DockPlayModeTests)
if exist "%LOGS%\playmode.xml" del "%LOGS%\playmode.xml"
"%UNITY%" -batchmode -projectPath "%PROJ%" -logFile "%LOGS%\playmode.log" -runTests -testPlatform PlayMode -testFilter BorderRepair.Tests.Unit07DockPlayModeTests -testResults "%LOGS%\playmode.xml"
if errorlevel 1 goto :fail

if /i "%~1"=="nocapture" goto :done
if /i "%~2"=="nocapture" goto :done
echo [4/4] Play mode capture with editor window (screenshots, draw calls, frame time)
"%UNITY%" -projectPath "%PROJ%" -logFile "%LOGS%\play_capture.log" -executeMethod BorderRepair.EditorTools.Unit07DockPlayCapture.Begin
if errorlevel 1 goto :fail

:done
echo All steps passed.
if /i not "%~1"=="nopause" pause
exit /b 0

:fail
echo FAILED (see %LOGS%)
if /i not "%~1"=="nopause" pause
exit /b 1

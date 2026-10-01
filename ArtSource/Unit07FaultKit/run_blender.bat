@echo off
rem 生成七号左引擎故障美术包：.blend、FBX、贴图、统计、检查、对比渲染。加参数 norender 只生成不渲染。
chcp 65001 >nul
set "HERE=%~dp0"
set "EXTRA="
if /i "%~1"=="norender" set "EXTRA=-- --norender"
echo Running Blender... (log: %HERE%build.log)
"D:\steam\steamapps\common\Blender\blender.exe" -b --factory-startup --python "%HERE%build_unit07_fault_kit.py" %EXTRA% > "%HERE%build.log" 2>&1
set "BLENDER_EXIT=%ERRORLEVEL%"
echo Exit code: %BLENDER_EXIT%
if /i not "%~2"=="nopause" pause
exit /b %BLENDER_EXIT%

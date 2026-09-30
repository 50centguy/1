@echo off
rem 生成 UNIT 07 维修座资产包：.blend、FBX、贴图、统计、检查报告、渲染图。加参数 norender 只生成不渲染。
chcp 65001 >nul
set "HERE=%~dp0"
set "TEMP=%HERE%_tmp"
set "TMP=%HERE%_tmp"
if not exist "%TEMP%" mkdir "%TEMP%"
set "EXTRA="
if /i "%~1"=="norender" set "EXTRA=-- --norender"
echo Running Blender... (log: %HERE%blender_run.log)
"D:\steam\steamapps\common\Blender\blender.exe" -b --factory-startup --python "%HERE%build_unit07_dock.py" %EXTRA% > "%HERE%blender_run.log" 2>&1
set "BLENDER_EXIT=%ERRORLEVEL%"
echo Exit code: %BLENDER_EXIT%
>> "%HERE%blender_run.log" echo Exit code: %BLENDER_EXIT%
if /i not "%~2"=="nopause" pause
exit /b %BLENDER_EXIT%

@echo off
rem Build SalvageDrone assets with a local temp directory and log file.
chcp 65001 >nul
set "HERE=%~dp0"
set "TEMP=%HERE%_tmp"
set "TMP=%HERE%_tmp"
if not exist "%TEMP%" mkdir "%TEMP%"
echo Running Blender... (log: %HERE%blender_run.log)
"D:\steam\steamapps\common\Blender\blender.exe" -b --factory-startup --python "%HERE%build_salvagedrone.py" > "%HERE%blender_run.log" 2>&1
set "BLENDER_EXIT=%ERRORLEVEL%"
echo Exit code: %BLENDER_EXIT%
>> "%HERE%blender_run.log" echo Exit code: %BLENDER_EXIT%
pause

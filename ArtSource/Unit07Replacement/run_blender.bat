@echo off
rem 生成 UNIT 07 左引擎替换补件：连接器核对、断开线缆 / 固定插座 / 新件贴图、.blend、FBX、渲染图。加参数 norender 只生成不渲染。
chcp 65001 >nul
set "HERE=%~dp0"
set "B=D:\steam\steamapps\common\Blender\blender.exe"
"%B%" -b --factory-startup --python "%HERE%analyze_connectors.py" > "%HERE%analyze_run.log" 2>&1
"%B%" -b --factory-startup --python "%HERE%build_replacement_kit.py" -- --norender > "%HERE%build_run.log" 2>&1
if /i not "%~1"=="norender" "%B%" -b --factory-startup --python "%HERE%render_kit.py" > "%HERE%render_run.log" 2>&1
echo Exit code: %ERRORLEVEL%
if /i not "%~2"=="nopause" pause

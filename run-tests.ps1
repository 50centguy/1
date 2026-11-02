#requires -Version 5.1
<#
  Unity 测试运行器 (ASCII only)
  用法:
    .\run-tests.ps1 -Platform EditMode -Filter DirtMaskTests,CleanableSurfaceTests
    .\run-tests.ps1 -Platform PlayMode -Filter MainSceneCleaningTests
#>
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('EditMode','PlayMode')]
    [string]$Platform,

    [string]$Filter = '',

    [string]$LogDir = 'D:\unity-logs',

    [switch]$KeepResults
)

$ErrorActionPreference = 'Stop'
$projectPath = $PSScriptRoot

# --- Unity 路径解析 (读项目已保存的编辑器路径, 找不到再回落到默认) ---
$unity = $null
$candidates = @(
    'C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe',
    'C:\Program Files\Unity\Hub\Editor\2022.3.62f1\Editor\Unity.exe'
)
foreach ($c in $candidates) {
    if (Test-Path $c) { $unity = $c; break }
}
if (-not $unity) {
    $hubRoot = 'C:\Program Files\Unity\Hub\Editor'
    if (Test-Path $hubRoot) {
        $found = Get-ChildItem $hubRoot -Directory -ErrorAction SilentlyContinue |
                 Sort-Object Name -Descending |
                 ForEach-Object { Join-Path $_.FullName 'Editor\Unity.exe' } |
                 Where-Object { Test-Path $_ } |
                 Select-Object -First 1
        if ($found) { $unity = $found }
    }
}
if (-not $unity) { throw 'Unity.exe not found under Unity Hub.' }

if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }

$stamp      = Get-Date -Format 'yyyyMMdd-HHmmss'
$safeFilter = ($Filter -replace '[^A-Za-z0-9_\-]', '_')
if (-not $safeFilter) { $safeFilter = 'All' }
$logFile    = Join-Path $LogDir ("test-{0}-{1}-{2}.log" -f $Platform, $safeFilter, $stamp)
$resFile    = Join-Path $LogDir ("test-{0}-{1}-{2}.xml" -f $Platform, $safeFilter, $stamp)

# --- 清理残留锁 ---
$lock = Join-Path $projectPath 'Temp\UnityLockfile'
if (Test-Path $lock) {
    try { Remove-Item $lock -Force -ErrorAction Stop; Write-Host '[lock] removed stale UnityLockfile' }
    catch { Write-Host '[lock] could not remove (Unity may be open): ' + $_.Exception.Message }
}

$unityArgs = @(
    '-batchmode'
    '-projectPath', $projectPath
    '-runTests'
    '-testPlatform', $Platform
    '-testResults', $resFile
    '-logFile', $logFile
)
# PlayMode capture tests need a real graphics device; only EditMode is safe without one.
if ($Platform -eq 'EditMode') { $unityArgs += '-nographics' }
# Unity treats the value after -testFilter as ONE group name, so a comma list never matches.
# Each class therefore gets its own -testFilter pair.
if ($Filter) {
    foreach ($one in ($Filter -split ',')) {
        $name = $one.Trim()
        if ($name) { $unityArgs += @('-testFilter', $name) }
    }
}

$filterLabel = '<all>'
if ($Filter) { $filterLabel = $Filter }
Write-Host ('[run] {0} filter={1}' -f $Platform, $filterLabel)
Write-Host ('[log] {0}' -f $logFile)

$sw = [Diagnostics.Stopwatch]::StartNew()
$p  = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -NoNewWindow
$p.WaitForExit()
$sw.Stop()

Write-Host ('[done] exit={0} elapsed={1:N1}s' -f $p.ExitCode, $sw.Elapsed.TotalSeconds)

# --- 解析结果 ---
if (Test-Path $resFile) {
    try {
        [xml]$x = Get-Content $resFile -Encoding UTF8 -Raw
        $run = $x.'test-run'
        Write-Host ('[result] total={0} passed={1} failed={2} skipped={3} result={4}' -f `
            $run.total, $run.passed, $run.failed, $run.skipped, $run.result)
        if ([int]$run.failed -gt 0) {
            Write-Host '[failures]'
            $x.SelectNodes('//test-case[@result="Failed"]') | ForEach-Object {
                Write-Host ('  FAIL {0}' -f $_.fullname)
                $m = $_.failure.message
                if ($m) { Write-Host ('       {0}' -f (($m -split "`n")[0])) }
                $st = $_.failure.'stack-trace'
                if ($st) {
                    ($st -split "`n") | Where-Object { $_ -match '\.cs:' } | Select-Object -First 3 |
                        ForEach-Object { Write-Host ('       {0}' -f $_.Trim()) }
                }
            }
        }
        if (-not $KeepResults) { }
    } catch {
        Write-Host ('[result] parse failed: {0}' -f $_.Exception.Message)
        Write-Host ('[result] raw file: {0}' -f $resFile)
    }
} else {
    Write-Host '[result] NO RESULT XML - tests likely did not run'
    Write-Host '[log tail]'
    if (Test-Path $logFile) {
        Get-Content $logFile -Encoding UTF8 -Tail 60 | ForEach-Object { Write-Host ('  ' + $_) }
    }
}

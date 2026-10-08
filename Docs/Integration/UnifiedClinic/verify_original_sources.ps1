$ErrorActionPreference = 'Stop'
$source = 'C:\Users\Administrator\UnityProjects\BorderRepairStation_slice'
$main = 'C:\Users\Administrator\UnityProjects\BorderRepairStation'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'source_snapshot.json') -Raw | ConvertFrom-Json
$rows = @(foreach ($entry in $manifest) {
    $path = Join-Path $source $entry.path
    $actual = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } else { 'MISSING' }
    [pscustomobject]@{ path = $entry.path; expected = $entry.sha256; actual = $actual; unchanged = ($actual -eq $entry.sha256) }
})
$mainHead = (& git -C $main rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read original main-workspace HEAD' }
$sourceHead = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot read original source-workspace HEAD' }
$report = [pscustomobject]@{
    timestampUtc = [DateTime]::UtcNow.ToString('O')
    sourceWorkspace = $source
    mainWorkspace = $main
    sourceHead = $sourceHead
    mainHead = $mainHead
    sourceHeadUnchanged = ($sourceHead -eq '088fac4e43f662f6bf09a31102fd0cb992340607')
    mainHeadUnchanged = ($mainHead -eq '2faf27229abb7986d4127a263b69503a9580b8ce')
    files = $rows
    limitations = @('The 34-file source snapshot is checked byte-for-byte. Original main-workspace HEAD is checked; this script does not claim a complete byte inventory of all its uncommitted files.')
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'original_workspace_verification.json') -Encoding UTF8
Write-Output ('Snapshot unchanged: ' + @($rows | Where-Object unchanged).Count + '/' + $rows.Count)
Write-Output ('Original HEADs unchanged: ' + $report.sourceHeadUnchanged + '/' + $report.mainHeadUnchanged)
if (@($rows | Where-Object { -not $_.unchanged }).Count -ne 0 -or -not $report.sourceHeadUnchanged -or -not $report.mainHeadUnchanged) {
    throw 'Original workspace differs from the pre-integration checkpoint; investigate without reverting user changes.'
}

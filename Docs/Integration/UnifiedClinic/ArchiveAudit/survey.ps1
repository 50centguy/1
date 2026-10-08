param([string]$ProjectRoot = 'C:/Users/Administrator/Documents/Codex/clinic-int')
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).Path
$output = $PSScriptRoot
$textExtensions = @('.unity','.prefab','.asset','.controller','.overridecontroller','.anim','.mat','.meta','.cs','.shader','.shadergraph','.shadersubgraph','.json','.asmdef','.asmref','.xml','.txt','.md','.uxml','.uss','.inputactions','.hlsl','.cginc','.compute')
$files = @(foreach ($dir in @('Assets','Packages','ProjectSettings')) {
    Get-ChildItem -LiteralPath (Join-Path $root $dir) -Recurse -File
})
function Relative([string]$path) { $path.Substring($root.Length + 1).Replace('\','/') }
$guidPaths = @{}
$duplicates = @()
foreach ($file in $files | Where-Object Extension -eq '.meta') {
    $raw = [IO.File]::ReadAllText($file.FullName)
    $m = [regex]::Match($raw, '(?m)^guid:\s*([a-fA-F0-9]{32})\s*$')
    if (!$m.Success) { continue }
    $guid = $m.Groups[1].Value.ToLowerInvariant()
    $asset = (Relative $file.FullName) -replace '\.meta$', ''
    if ($guidPaths.ContainsKey($guid)) { $duplicates += [pscustomobject]@{ guid=$guid; paths=@($guidPaths[$guid],$asset) } }
    $guidPaths[$guid] = $asset
}
$edges = @{}
$guidRefs = @{}
$sources = @{}
foreach ($file in $files | Where-Object { $_.Extension -in $textExtensions }) {
    $path = Relative $file.FullName
    $raw = [IO.File]::ReadAllText($file.FullName)
    if ($raw.Contains([char]0)) { continue }
    $guids = @([regex]::Matches($raw, 'guid:\s*([a-fA-F0-9]{32})') | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() } | Sort-Object -Unique)
    $asset = $path -replace '\.meta$', ''
    foreach ($guid in $guids) {
        if ($path.EndsWith('.meta') -and $guidPaths[$guid] -eq $asset) { continue }
        if (!$edges.ContainsKey($asset)) { $edges[$asset] = [Collections.Generic.HashSet[string]]::new() }
        [void]$edges[$asset].Add($guid)
        if (!$guidRefs.ContainsKey($guid)) { $guidRefs[$guid] = [Collections.Generic.HashSet[string]]::new() }
        [void]$guidRefs[$guid].Add($path)
    }
    if ($file.Extension -in @('.cs','.asset','.json','.asmdef','.asmref','.inputactions')) { $sources[$path] = $raw }
}
$scenes = @()
$allSceneAssets = [Collections.Generic.HashSet[string]]::new()
foreach ($file in $files | Where-Object Extension -eq '.unity' | Sort-Object FullName) {
    $path = Relative $file.FullName
    $sceneGuid = @($guidPaths.Keys | Where-Object { $guidPaths[$_] -eq $path }) | Select-Object -First 1
    $visited = [Collections.Generic.HashSet[string]]::new()
    $unknown = [Collections.Generic.HashSet[string]]::new()
    $queue = [Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($path)
    while ($queue.Count -gt 0) {
        $next = $queue.Dequeue()
        if (!$visited.Add($next)) { continue }
        if (!$edges.ContainsKey($next)) { continue }
        foreach ($guid in $edges[$next]) {
            if ($guidPaths.ContainsKey($guid)) { $queue.Enqueue($guidPaths[$guid]) }
            elseif ($guid -notmatch '^0{16}[def]0{15}$' -and $guid -ne ('0' * 32)) { [void]$unknown.Add($guid) }
        }
    }
    [void]$visited.Remove($path)
    foreach ($dep in $visited) { [void]$allSceneAssets.Add($dep) }
    $name = [IO.Path]::GetFileNameWithoutExtension($path)
    $references = @()
    foreach ($sourcePath in ($sources.Keys | Sort-Object)) {
        $lineNumber = 0
        foreach ($line in ($sources[$sourcePath] -split '\r?\n')) {
            $lineNumber++
            if ($line.Contains($name) -or ($sceneGuid -and $line.Contains($sceneGuid))) {
                $references += [pscustomobject]@{ path=$sourcePath; line=$lineNumber; text=$line.Trim() }
            }
        }
    }
    $scenes += [pscustomobject]@{
        path=$path; guid=$sceneGuid; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        metaSha256=if (Test-Path -LiteralPath ($file.FullName+'.meta')) { (Get-FileHash -LiteralPath ($file.FullName+'.meta') -Algorithm SHA256).Hash } else { $null }
        directGuids=@($edges[$path] | Sort-Object); textualClosure=@($visited | Sort-Object)
        unresolvedGuids=@($unknown | Sort-Object); inboundGuidFiles=@($guidRefs[$sceneGuid] | Sort-Object)
        nameOrGuidReferences=$references
    }
}
$buildRaw = [IO.File]::ReadAllText((Join-Path $root 'ProjectSettings/EditorBuildSettings.asset'))
$buildEntries = @([regex]::Matches($buildRaw,'(?m)^\s*- enabled:\s*(\d+)\r?\n\s*path:\s*(.+)\r?\n\s*guid:\s*([a-fA-F0-9]{32})') | ForEach-Object {
    $path = $_.Groups[2].Value.Trim()
    $guid = $_.Groups[3].Value.ToLowerInvariant()
    $actual = @($scenes | Where-Object path -eq $path).guid
    [pscustomobject]@{ enabled=$_.Groups[1].Value; path=$path; buildGuid=$guid; currentGuid=$actual; matches=($guid -eq $actual) }
})
$dynamicReferences = @()
foreach ($sourcePath in ($sources.Keys | Sort-Object)) {
    $lineNumber = 0
    foreach ($line in ($sources[$sourcePath] -split '\r?\n')) {
        $lineNumber++
        if ($line -match '(LoadScene|OpenScene|CopyAsset|BuildScenes|ScenePath|SceneDir|SourceScene|Resources\.Load|Addressables|AssetBundle)') {
            $dynamicReferences += [pscustomobject]@{ path=$sourcePath; line=$lineNumber; text=$line.Trim() }
        }
    }
}
$report = [ordered]@{
    generatedUtc=[DateTime]::UtcNow.ToString('o'); project=$root
    method='INCOMPLETE textual GUID graph only; not Unity AssetDatabase.GetDependencies; concurrent files are not an atomic snapshot'
    duplicateGuids=$duplicates; buildEntries=$buildEntries; scenes=$scenes
    allSceneTextualDependencies=@($allSceneAssets | Sort-Object); dynamicReferences=$dynamicReferences
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'survey.json') -Encoding utf8
$references = foreach ($r in $dynamicReferences) { '{0}:{1}: {2}' -f $r.path,$r.line,$r.text }
$references | Set-Content -LiteralPath (Join-Path $output 'scene_path_references.txt') -Encoding utf8
$scenes | ForEach-Object { [pscustomobject]@{path=$_.path; guid=$_.guid; direct=$_.directGuids.Count; closure=$_.textualClosure.Count; unresolved=$_.unresolvedGuids.Count; inbound=($_.inboundGuidFiles -join ', ')} } | Format-Table -AutoSize
$buildEntries | Format-Table -AutoSize
Write-Output ('Duplicate metadata GUIDs: ' + $duplicates.Count)

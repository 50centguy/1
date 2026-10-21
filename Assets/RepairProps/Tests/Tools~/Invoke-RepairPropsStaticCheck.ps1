param(
    [string] $UnityData = 'C:/Program Files/Unity/Hub/Editor/6000.0.84f1/Editor/Data',
    [string] $ReferenceProject = 'C:/Users/Administrator/Documents/Codex/clinic-bench',
    [string[]] $AdditionalSources = @(),
    [string[]] $AdditionalReferences = @()
)

# Checks source against existing reference assemblies. Never starts Unity or writes to Library.
# The ignored Tools~ directory holds a reusable csc response file; no assembly is emitted here.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../..'))
Add-Type -Path (Join-Path $UnityData 'DotNetSdkRoslyn/Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $UnityData 'DotNetSdkRoslyn/Microsoft.CodeAnalysis.CSharp.dll')

$referencePaths = @((Get-ChildItem (Join-Path $UnityData 'NetStandard/ref/2.1.0') -Filter '*.dll').FullName)
$referencePaths += @((Get-ChildItem (Join-Path $UnityData 'NetStandard/compat/2.1.0/shims/netfx') -Filter '*.dll').FullName)
$referencePaths += @((Get-ChildItem (Join-Path $UnityData 'Managed/UnityEngine') -Filter '*.dll').FullName)
$referencePaths += @((Get-ChildItem (Join-Path $ReferenceProject 'Library/ScriptAssemblies') -Filter '*.dll' |
    Where-Object { $_.Name -notlike 'BorderRepair.RepairProps*' }).FullName)
$referencePaths += @((Get-ChildItem (Join-Path $ReferenceProject 'Library/PackageCache/com.unity.ext.nunit*/net40/unity-custom/nunit.framework.dll')).FullName)
$referencePaths += $AdditionalReferences
$referencePaths = @($referencePaths | Sort-Object -Unique)

$sourcePaths = @((Get-ChildItem (Join-Path $projectRoot 'Assets/RepairProps/Runtime') -Filter '*.cs').FullName)
$sourcePaths += @((Get-ChildItem (Join-Path $projectRoot 'Assets/RepairProps/Tests') -Filter '*.cs' -Recurse).FullName)
$sourcePaths += Join-Path $projectRoot 'Assets/RepairProps/Editor/RepairPropCaseFactory.cs'
foreach ($source in $AdditionalSources)
{
    $sourcePaths += if ([IO.Path]::IsPathRooted($source)) { $source } else { Join-Path $projectRoot $source }
}
$sourcePaths = @($sourcePaths | Sort-Object -Unique)

$responsePath = Join-Path $PSScriptRoot 'RepairPropsStaticCheck.rsp'
$outputPath = Join-Path $PSScriptRoot 'RepairPropsStaticCheck.dll'
$response = @('/nologo', '/target:library', '/nostdlib+', '/langversion:latest', '/define:UNITY_EDITOR;UNITY_INCLUDE_TESTS', ('/out:"' + $outputPath + '"'))
$response += @($referencePaths | ForEach-Object { '/reference:"' + $_ + '"' })
$response += @($sourcePaths | ForEach-Object { '"' + $_ + '"' })
[IO.File]::WriteAllLines($responsePath, [string[]]$response)

$references = [Microsoft.CodeAnalysis.MetadataReference[]]@($referencePaths | ForEach-Object {
    [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_)
})
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithPreprocessorSymbols([string[]]@('UNITY_EDITOR', 'UNITY_INCLUDE_TESTS'))
$trees = [Microsoft.CodeAnalysis.SyntaxTree[]]@($sourcePaths | ForEach-Object {
    [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($_), $parseOptions, $_)
})
$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('RepairPropsStaticCheck', $trees, $references, $options)
$diagnostics = @($compilation.GetDiagnostics() | Where-Object { $_.Severity -in @('Error', 'Warning') })
$diagnostics | ForEach-Object { $_.ToString() }
Write-Output "Checked $($sourcePaths.Count) source files: $($diagnostics.Count) warning/error diagnostics. Unity not started; tests not executed."
Write-Output "Compiler response file: $responsePath"
if (@($diagnostics | Where-Object { $_.Severity -eq 'Error' }).Count -gt 0) { throw 'Static compilation check failed.' }

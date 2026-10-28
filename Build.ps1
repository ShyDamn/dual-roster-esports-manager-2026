param([Parameter(Mandatory=$true)][string]$GamePath, [string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$bepinex=Join-Path (Resolve-Path -LiteralPath $GamePath).Path 'BepInEx'
if (!(Test-Path (Join-Path $bepinex 'core\BepInEx.Unity.IL2CPP.dll'))) { throw 'Install the supported Workshop package first.' }
foreach ($project in @('src\DualRoster.csproj','loader\DualRoster.Workshop.csproj')) {
    & $Dotnet build (Join-Path $PSScriptRoot $project) -c Release "-p:BepInExRoot=$bepinex" "-p:InteropRoot=$bepinex\interop" -o (Join-Path $PSScriptRoot 'build')
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
}
Write-Host 'Both production DLLs are in build/. This script does not install them.'

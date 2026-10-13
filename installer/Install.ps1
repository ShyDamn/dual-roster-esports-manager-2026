param([string]$GamePath, [switch]$VerifyOnly, [string]$LoaderArchive)
$ErrorActionPreference = 'Stop'
$expectedGame = 'A4D500C32CC3FB5457ADCE6089ADD87165706732BAB96CDAB4FEC97AE32A78AA'
$expectedLoader = 'F4CC496BD098A0DF4164B81E3737297707F13A47C2478DBA2F60EEFAB784817A'
$loaderUrl = 'https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip'

function Inside([string]$Root, [string]$Relative) {
    $base = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $path = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (!$path.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package path.' }
    return $path
}

if (!$GamePath) {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    $libraries = @($steam)
    if ($steam -and (Test-Path (Join-Path $steam 'steamapps\libraryfolders.vdf'))) {
        $vdf = Get-Content (Join-Path $steam 'steamapps\libraryfolders.vdf') -Raw
        foreach ($match in [regex]::Matches($vdf, '"path"\s+"([^"]+)"')) { $libraries += $match.Groups[1].Value.Replace('\\','\') }
    }
    foreach ($library in $libraries | Select-Object -Unique) {
        if (!$library) { continue }
        $candidate = Join-Path $library 'steamapps\common\Esports Manager 2026'
        if (Test-Path (Join-Path $candidate 'EsportsManager.exe')) { $GamePath = $candidate; break }
    }
    if (!$GamePath) { $GamePath = Read-Host 'Full path to the Esports Manager 2026 game folder' }
}
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path
$assembly = Inside $GamePath 'GameAssembly.dll'
if (!(Test-Path -LiteralPath $assembly) -or (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $expectedGame) {
    throw 'Unsupported game version. This release requires Steam build 25869291. No files were installed.'
}
$manifest = Get-Content (Join-Path $PSScriptRoot 'payload-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $source = Inside (Join-Path $PSScriptRoot 'payload') $entry.Path
    if (!(Test-Path -LiteralPath $source) -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.Hash) {
        throw "Package verification failed: $($entry.Path)"
    }
}
$runtime = Get-Content (Join-Path $PSScriptRoot 'runtime\workshop-loader.json') -Raw | ConvertFrom-Json
if ($runtime.Schema -ne 1 -or $runtime.LoaderApi -ne 1 -or $runtime.WorkshopId -ne 3819071177 -or
    $runtime.CompatibilityId -ne 'em2026-25869291-interop107-v1' -or
    (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'runtime\DualRoster.dll')).Hash -ne $runtime.Sha256) {
    throw 'Workshop gameplay package verification failed.'
}
if ($VerifyOnly) { Write-Host 'Game build and all package hashes verified.'; exit 0 }
$gameExe = Inside $GamePath 'EsportsManager.exe'
if (@(Get-Process EsportsManager -ErrorAction SilentlyContinue | Where-Object { !$_.Path -or $_.Path -eq $gameExe }).Count -gt 0) { throw 'Close Esports Manager before installing.' }
$legacy = Inside $GamePath 'BepInEx\plugins\DualRoster\DualRoster.dll'
$pluginRoot = Inside $GamePath 'BepInEx\plugins'
if (Test-Path -LiteralPath $pluginRoot) {
    $duplicates = @(Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter 'DualRoster.dll' | Where-Object { $_.FullName -ne $legacy })
    if ($duplicates.Count -gt 0) { throw 'Another DualRoster.dll exists in BepInEx/plugins. Back it up outside plugins before installing the Workshop loader.' }
}
$core = Inside $GamePath 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'
$newLoader = !(Test-Path -LiteralPath $core)
if (!$newLoader -and (Get-FileHash -LiteralPath $core).Hash -ne '46CF1EF802BDF8CB6587FC1E4D98F7AF1073A60487B39A64E230E6F6E4C23AEC') {
    throw 'A different BepInEx version is installed. Keep your existing setup backed up and install this mod into a clean supported game folder.'
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$backup = Inside $GamePath ('DualRosterBackups\' + $stamp)
$cache = Join-Path ([IO.Path]::GetTempPath()) ('DualRoster-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$loaderFiles = @()
if ($newLoader) {
    if (!$LoaderArchive) {
        $LoaderArchive = Join-Path $cache 'BepInEx.zip'
        Write-Host 'Downloading official BepInEx build 788 (about 34 MB)...'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $loaderUrl -OutFile $LoaderArchive
    }
    if ((Get-FileHash -LiteralPath $LoaderArchive).Hash -ne $expectedLoader) { throw 'BepInEx download checksum mismatch.' }
    $expanded = Join-Path $cache 'loader'
    Expand-Archive -LiteralPath $LoaderArchive -DestinationPath $expanded
    $loaderFiles = @(Get-ChildItem -LiteralPath $expanded -Recurse -File | ForEach-Object {
        [pscustomobject]@{ Path = $_.FullName.Substring($expanded.Length + 1); Source = $_.FullName }
    })
}
$files = @{}
foreach ($file in $loaderFiles) { $files[$file.Path] = $file.Source }
foreach ($entry in $manifest) { $files[$entry.Path] = Inside (Join-Path $PSScriptRoot 'payload') $entry.Path }
$configPath = Inside $GamePath 'BepInEx\config\BepInEx.cfg'
$config = if (Test-Path -LiteralPath $configPath) { Get-Content -LiteralPath $configPath -Raw } else { "[IL2CPP]`r`nUpdateInteropAssemblies = false`r`n" }
if ($config -match '(?m)^\s*UpdateInteropAssemblies\s*=') {
    $config = [regex]::Replace($config, '(?m)^\s*UpdateInteropAssemblies\s*=.*$', 'UpdateInteropAssemblies = false')
} elseif ($config -match '(?m)^\[IL2CPP\]\s*$') {
    $config = [regex]::Replace($config, '(?m)^\[IL2CPP\]\s*$', "[IL2CPP]`r`nUpdateInteropAssemblies = false")
} else { $config += "`r`n[IL2CPP]`r`nUpdateInteropAssemblies = false`r`n" }
$generatedConfig = Join-Path $cache 'BepInEx.cfg'
[IO.File]::WriteAllText($generatedConfig, $config, (New-Object Text.UTF8Encoding($false)))
$files['BepInEx\config\BepInEx.cfg'] = $generatedConfig
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$changes = @()
foreach ($relative in $files.Keys | Sort-Object) {
    $target = Inside $GamePath $relative
    $existed = Test-Path -LiteralPath $target
    if ($existed) {
        $copy = Inside $backup $relative
        New-Item -ItemType Directory -Path (Split-Path $copy) -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $copy
    }
    $changes += [pscustomobject]@{ Path = $relative; Existed = $existed; InstalledHash = (Get-FileHash -LiteralPath $files[$relative]).Hash }
}
if (Test-Path -LiteralPath $legacy) {
    $relative = 'BepInEx\plugins\DualRoster\DualRoster.dll'
    $copy = Inside $backup $relative
    New-Item -ItemType Directory -Path (Split-Path $copy) -Force | Out-Null
    Copy-Item -LiteralPath $legacy -Destination $copy
    $changes += [pscustomobject]@{ Path = $relative; Existed = $true; InstalledHash = $null }
}
$changes | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $backup 'installation.json') -Encoding UTF8
try {
    foreach ($relative in $files.Keys | Sort-Object) {
        $target = Inside $GamePath $relative
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $files[$relative] -Destination $target -Force
    }
    if (Test-Path -LiteralPath $legacy) { Remove-Item -LiteralPath $legacy -Force }
} catch {
    foreach ($entry in $changes) {
        $target = Inside $GamePath $entry.Path
        if ($entry.Existed) { Copy-Item -LiteralPath (Inside $backup $entry.Path) -Destination $target -Force }
        elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    throw
}
Write-Host 'Dual Roster Workshop Loader 0.3.0 installed. Subscribe to item 3819071177 and start through Steam.'
Write-Host 'Ordinary mod updates now load automatically on the next game launch. The main menu shows status.'
Write-Host "Original loader files backed up to: $backup"
Write-Host 'Career saves were not changed. Keep a separate backup before playing with mods.'

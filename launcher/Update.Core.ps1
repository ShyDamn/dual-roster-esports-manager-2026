Set-StrictMode -Version 2
$ErrorActionPreference='Stop'

function Get-DrHash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function ConvertTo-DrArgument([AllowEmptyString()][string]$Value) {
    if($Value.Length -and $Value -notmatch '[\s"]') { return $Value }
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"'+$escaped+'"'
}
function Get-DrInside([string]$Root,[string]$Relative) {
    if([IO.Path]::IsPathRooted($Relative)) { throw 'Absolute package paths are not allowed.' }
    $base=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
    $path=[IO.Path]::GetFullPath((Join-Path $base $Relative))
    if(!$path.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package path escapes its root.' }
    $cursor=$path
    while($cursor.Length -gt $base.Length) {
        if(Test-Path -LiteralPath $cursor) {
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse points are not allowed in update paths.' }
        }
        $cursor=Split-Path $cursor -Parent
    }
    return $path
}
function Test-DrAllowed([string]$Path) {
    return $Path -match '^(BepInEx\\(interop\\[A-Za-z0-9._-]+|core\\[A-Za-z0-9._-]+\.dll|plugins\\DualRosterWorkshop\\DualRoster\.Workshop\.dll)|DualRoster\\(Launch\.ps1|Update\.Core\.ps1|SteamBridge\.cs|compatibility\.json))$'
}
function Read-DrPackage([string]$Package,[string]$GameRoot) {
    $manifestPath=Get-DrInside $Package 'runtime\workshop-loader.json'
    if((Get-Item -LiteralPath $manifestPath).Length -gt 16384) { throw 'Invalid package manifest size.' }
    $manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $version=[version]'0.0'
    if($manifest.Schema -ne 1 -or $manifest.LoaderApi -ne 1 -or $manifest.WorkshopId -ne 3819071177 -or
       ![version]::TryParse([string]$manifest.Version,[ref]$version) -or $manifest.BootstrapApi -ne 1 -or
       $manifest.GameHash -notmatch '^[A-Fa-f0-9]{64}$' -or
       $manifest.GameHash -ne (Get-DrHash (Get-DrInside $GameRoot 'GameAssembly.dll'))) { throw 'No compatible published package for this game build.' }
    if($manifest.Sha256 -ne (Get-DrHash (Get-DrInside $Package 'runtime\DualRoster.dll'))) { throw 'Gameplay package checksum mismatch.' }
    $fileManifest=Get-DrInside $Package 'payload-manifest.json'
    if((Get-Item -LiteralPath $fileManifest).Length -gt 1048576) { throw 'Invalid payload manifest size.' }
    $entries=Get-Content -LiteralPath $fileManifest -Raw | ConvertFrom-Json
    if($entries -isnot [array]) { $entries=@($entries) }
    if(!$entries.Count -or $entries.Count -gt 1024) { throw 'Invalid payload entry count.' }
    $seen=@{}
    foreach($entry in $entries) {
        $relative=([string]$entry.Path).Replace('/','\')
        if(!(Test-DrAllowed $relative) -or $seen.ContainsKey($relative) -or $entry.Hash -notmatch '^[A-Fa-f0-9]{64}$') { throw "Invalid payload entry: $relative" }
        $seen[$relative]=$true
        $source=Get-DrInside (Join-Path $Package 'payload') $relative
        $null=Get-DrInside $GameRoot $relative
        if(!(Test-Path -LiteralPath $source -PathType Leaf) -or (Get-DrHash $source) -ne $entry.Hash) { throw "Payload checksum mismatch: $relative" }
        $entry.Path=$relative
    }
    return [pscustomobject]@{ Manifest=$manifest; Entries=$entries }
}
function Restore-DrUpdate([string]$GameRoot,[string]$Backup) {
    $record=Get-Content -LiteralPath (Join-Path $Backup 'update.json') -Raw | ConvertFrom-Json
    if($record.Product -ne 'DualRoster' -or $record.GameRoot -ne [IO.Path]::GetFullPath($GameRoot)) { throw 'Invalid recovery record.' }
    foreach($entry in $record.Changes) {
        if(!(Test-DrAllowed $entry.Path)) { throw 'Invalid recovery path.' }
        $target=Get-DrInside $GameRoot $entry.Path
        if(Test-Path -LiteralPath $target) {
            $current=Get-DrHash $target
            if($current -eq $entry.OldHash) { continue }
            if($current -ne $entry.Hash) { throw "Update recovery found an externally changed file: $($entry.Path)" }
        }
        if($entry.Existed) {
            $source=Get-DrInside (Join-Path $Backup 'original') $entry.Path
            if((Get-DrHash $source) -ne $entry.OldHash) { throw 'Backup checksum mismatch.' }
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        } elseif(Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
    }
    $record.State='RolledBack'
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Backup 'update.json') -Encoding UTF8
}
function Repair-DrInterruptedUpdate([string]$GameRoot) {
    $root=Get-DrInside $GameRoot 'DualRosterBackups'
    if(!(Test-Path -LiteralPath $root)) { return }
    foreach($directory in Get-ChildItem -LiteralPath $root -Directory -Filter 'update-*') {
        if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Update recovery directory is a reparse point.' }
        $record=Join-Path $directory.FullName 'update.json'
        if(Test-Path -LiteralPath $record) {
            $item=Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
            if($item.State -eq 'Applying') { Restore-DrUpdate $GameRoot $directory.FullName }
        }
    }
}
function Invoke-DrUpdate([string]$Package,[string]$GameRoot,[string]$SaveRoot,[scriptblock]$BeforeWrite,[scriptblock]$Progress) {
    $GameRoot=[IO.Path]::GetFullPath($GameRoot)
    $exe=Get-DrInside $GameRoot 'EsportsManager.exe'
    if(@(Get-Process EsportsManager -ErrorAction SilentlyContinue | Where-Object { !$_.Path -or $_.Path -eq $exe }).Count) { throw 'Close the game before updating its components.' }
    Repair-DrInterruptedUpdate $GameRoot
    $packageInfo=Read-DrPackage $Package $GameRoot
    $changed=@($packageInfo.Entries | Where-Object {
        $target=Get-DrInside $GameRoot $_.Path
        !(Test-Path -LiteralPath $target) -or (Get-DrHash $target) -ne $_.Hash
    })
    if(!$changed.Count) { return [pscustomobject]@{ Changed=0; Backup=$null; Version=$packageInfo.Manifest.Version } }
    $backup=Get-DrInside $GameRoot ('DualRosterBackups\update-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')+'-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
    $changes=@()
    foreach($entry in $changed) {
        if($Progress) { & $Progress 0 $changed.Count }
        $target=Get-DrInside $GameRoot $entry.Path
        $exists=Test-Path -LiteralPath $target
        $oldHash=if($exists) { Get-DrHash $target } else { '' }
        if($exists) {
            $original=Get-DrInside (Join-Path $backup 'original') $entry.Path
            New-Item -ItemType Directory -Path (Split-Path $original) -Force | Out-Null
            Copy-Item -LiteralPath $target -Destination $original
        }
        $staged=Get-DrInside (Join-Path $backup 'staged') $entry.Path
        New-Item -ItemType Directory -Path (Split-Path $staged) -Force | Out-Null
        Copy-Item -LiteralPath (Get-DrInside (Join-Path $Package 'payload') $entry.Path) -Destination $staged
        if((Get-DrHash $staged) -ne $entry.Hash) { throw 'Staged component checksum mismatch.' }
        $changes+=@([pscustomobject]@{Path=$entry.Path;Hash=$entry.Hash;Existed=$exists;OldHash=$oldHash})
    }
    if($SaveRoot -and (Test-Path -LiteralPath $SaveRoot)) {
        $saveBase=[IO.Path]::GetFullPath($SaveRoot).TrimEnd('\')+'\'
        if(($backup+'\').StartsWith($saveBase,[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be outside the save directory.' }
        foreach($file in Get-ChildItem -LiteralPath $SaveRoot -Recurse -File) {
            if($Progress) { & $Progress 0 $changes.Count }
            if($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Save backup contains a reparse point.' }
            $destination=Get-DrInside (Join-Path $backup 'Save') $file.FullName.Substring($saveBase.Length)
            New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }
    $record=[pscustomobject]@{Product='DualRoster';GameRoot=$GameRoot;Version=$packageInfo.Manifest.Version;State='Applying';Changes=$changes}
    $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'update.json') -Encoding UTF8
    try {
        $i=0
        foreach($entry in $changes) {
            if($BeforeWrite) { & $BeforeWrite $i $entry }
            $target=Get-DrInside $GameRoot $entry.Path
            New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
            $temporary=$target+'.dr-'+[guid]::NewGuid().ToString('N')+'.tmp'
            try {
                Copy-Item -LiteralPath (Get-DrInside (Join-Path $backup 'staged') $entry.Path) -Destination $temporary
                if(Test-Path -LiteralPath $target) { [IO.File]::Replace($temporary,$target,[NullString]::Value) }
                else { [IO.File]::Move($temporary,$target) }
            } finally { if(Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
            $i++
            if($Progress) { & $Progress $i $changes.Count }
        }
        $record.State='Complete'
        $record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $backup 'update.json') -Encoding UTF8
    } catch { Restore-DrUpdate $GameRoot $backup; throw }
    return [pscustomobject]@{ Changed=$changed.Count; Backup=$backup; Version=$packageInfo.Manifest.Version }
}

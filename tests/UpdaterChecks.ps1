param([Parameter(Mandatory=$true)][string]$WorkRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\launcher\Update.Core.ps1')
$root=Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ('updater-checks-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$script:Checks=0
function Check([bool]$Value,[string]$Name) { if(!$Value) { throw $Name }; $script:Checks++; Write-Output ('PASS '+$Name) }
function Reject([scriptblock]$Action,[string]$Name,[string]$Expected='') {
    $rejected=$false
    try { & $Action | Out-Null } catch {
        if($Expected -and $_.Exception.Message -notmatch $Expected) { throw "Unexpected failure in $Name : $($_.Exception.Message)" }
        $rejected=$true
    }
    Check $rejected $Name
}
function Fixture([string]$Name) {
    $base=Join-Path $root $Name; $game=Join-Path $base 'game'; $package=Join-Path $base 'package'; $save=Join-Path $base 'Save'
    foreach($path in @($game,(Join-Path $package 'runtime'),(Join-Path $package 'payload\BepInEx\plugins\DualRosterWorkshop'),$save)) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
    [IO.File]::WriteAllText((Join-Path $game 'GameAssembly.dll'),'test-binary')
    [IO.File]::WriteAllText((Join-Path $game 'EsportsManager.exe'),'not executable')
    [IO.File]::WriteAllText((Join-Path $save 'career.mpack'),'career unchanged')
    $relative='BepInEx\plugins\DualRosterWorkshop\DualRoster.Workshop.dll'
    $target=Get-DrInside $game $relative; New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    [IO.File]::WriteAllText($target,'old loader')
    [IO.File]::WriteAllText((Join-Path $package ('payload\'+$relative)),'new loader')
    [IO.File]::WriteAllText((Join-Path $package 'runtime\DualRoster.dll'),'gameplay')
    $manifest=[pscustomobject]@{Schema=1;LoaderApi=1;BootstrapApi=1;WorkshopId=[uint64]3819071177;Version='0.4.2';GameHash=(Get-DrHash (Join-Path $game 'GameAssembly.dll'));Sha256=(Get-DrHash (Join-Path $package 'runtime\DualRoster.dll'))}
    $manifest | ConvertTo-Json | Set-Content (Join-Path $package 'runtime\workshop-loader.json') -Encoding UTF8
    @([pscustomobject]@{Path=$relative;Hash=(Get-DrHash (Join-Path $package ('payload\'+$relative)))}) | ConvertTo-Json | Set-Content (Join-Path $package 'payload-manifest.json') -Encoding UTF8
    return [pscustomobject]@{Game=$game;Package=$package;Save=$save;Target=$target;Relative=$relative}
}
$fixture=Fixture 'normal'
$result=Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save
Check ($result.Changed -eq 1 -and [IO.File]::ReadAllText($fixture.Target) -eq 'new loader') 'Compatible update installed'
Check ([IO.File]::ReadAllText((Join-Path $result.Backup ('original\'+$fixture.Relative))) -eq 'old loader') 'Previous component backed up'
Check ([IO.File]::ReadAllText((Join-Path $result.Backup 'Save\career.mpack')) -eq 'career unchanged') 'Career snapshot created before replacement'
Check ([IO.File]::ReadAllText((Join-Path $fixture.Save 'career.mpack')) -eq 'career unchanged') 'Original career unchanged'
Check ((Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save).Changed -eq 0) 'Already installed package does not repeat backup or writes'

$fixture=Fixture 'older-workshop'
$compatibility=Join-Path $fixture.Game 'DualRoster\compatibility.json'
New-Item -ItemType Directory -Path (Split-Path $compatibility) -Force | Out-Null
@{Schema=1;GameHash=(Get-DrHash (Join-Path $fixture.Game 'GameAssembly.dll'));Version='99.0.0'} | ConvertTo-Json | Set-Content $compatibility -Encoding UTF8
$result=Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save
Check ($result.SkippedOlderPackage -and $result.Changed -eq 0) 'Older Workshop delivery never downgrades newer compatible components'
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'old loader' -and !(Test-Path (Join-Path $fixture.Game 'DualRosterBackups'))) 'Skipping an older package leaves files and career untouched'

$fixture=Fixture 'wrong-game'
[IO.File]::WriteAllText((Join-Path $fixture.Game 'GameAssembly.dll'),'another build')
Reject { Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save } 'Unknown game build rejected'
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'old loader') 'Unknown build leaves installed files unchanged'

$fixture=Fixture 'corrupt'
[IO.File]::WriteAllText((Join-Path $fixture.Package ('payload\'+$fixture.Relative)),'corrupted')
Reject { Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save } 'Corrupt payload rejected before installation'
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'old loader') 'Corrupt package leaves installed files unchanged'
[IO.File]::WriteAllText((Join-Path $fixture.Package 'runtime\DualRoster.dll'),'corrupt gameplay')
Reject { Read-DrPackage $fixture.Package $fixture.Game } 'Corrupt gameplay DLL rejected'

$fixture=Fixture 'traversal'
@([pscustomobject]@{Path='..\outside.dll';Hash=('A'*64)}) | ConvertTo-Json | Set-Content (Join-Path $fixture.Package 'payload-manifest.json') -Encoding UTF8
Reject { Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save } 'Path traversal rejected'
Reject { Get-DrInside $fixture.Game '..\escape' } 'Canonical paths stay inside the target root'
Check (!(Test-DrAllowed 'EsportsManager.exe') -and !(Test-DrAllowed 'GameAssembly.dll') -and !(Test-DrAllowed 'BepInEx\plugins\Other\Other.dll')) 'Updater cannot replace game binaries or unrelated plugins'

$fixture=Fixture 'duplicate'
$entry=[pscustomobject]@{Path=$fixture.Relative;Hash=(Get-DrHash (Join-Path $fixture.Package ('payload\'+$fixture.Relative)))}
@($entry,$entry) | ConvertTo-Json | Set-Content (Join-Path $fixture.Package 'payload-manifest.json') -Encoding UTF8
Reject { Read-DrPackage $fixture.Package $fixture.Game } 'Duplicate payload destinations rejected'

$fixture=Fixture 'rollback'
$newPath='BepInEx\interop\NewBinding.dll'
New-Item -ItemType Directory -Path (Join-Path $fixture.Package 'payload\BepInEx\interop') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture.Package ('payload\'+$newPath)),'new binding')
$entries=@([pscustomobject]@{Path=$newPath;Hash=(Get-DrHash (Join-Path $fixture.Package ('payload\'+$newPath)))},[pscustomobject]@{Path=$fixture.Relative;Hash=(Get-DrHash (Join-Path $fixture.Package ('payload\'+$fixture.Relative)))})
$entries | ConvertTo-Json | Set-Content (Join-Path $fixture.Package 'payload-manifest.json') -Encoding UTF8
Reject { Invoke-DrUpdate -Package $fixture.Package -GameRoot $fixture.Game -SaveRoot $fixture.Save -BeforeWrite { param($index,$entry) if($index -eq 1) { throw 'Injected disk failure' } } } 'Partial installation failure triggers rollback' 'Injected disk failure'
Check (!(Test-Path (Join-Path $fixture.Game $newPath)) -and [IO.File]::ReadAllText($fixture.Target) -eq 'old loader') 'Rollback removes only new files and restores previous contents'
$record=Get-ChildItem (Join-Path $fixture.Game 'DualRosterBackups') -Directory | Select-Object -First 1
Check ((Get-Content (Join-Path $record.FullName 'update.json') -Raw | ConvertFrom-Json).State -eq 'RolledBack') 'Rollback is recorded for recovery'

$fixture=Fixture 'rollback-overwrite'
$second='BepInEx\interop\Second.dll'
New-Item -ItemType Directory -Path (Join-Path $fixture.Package 'payload\BepInEx\interop') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture.Package ('payload\'+$second)),'second binding')
@([pscustomobject]@{Path=$fixture.Relative;Hash=(Get-DrHash (Join-Path $fixture.Package ('payload\'+$fixture.Relative)))},[pscustomobject]@{Path=$second;Hash=(Get-DrHash (Join-Path $fixture.Package ('payload\'+$second)))}) | ConvertTo-Json | Set-Content (Join-Path $fixture.Package 'payload-manifest.json') -Encoding UTF8
Reject { Invoke-DrUpdate -Package $fixture.Package -GameRoot $fixture.Game -SaveRoot $fixture.Save -BeforeWrite { param($index,$entry) if($index -eq 1) { throw 'Injected failure after overwrite' } } } 'Overwrite failure exercises rollback' 'Injected failure after overwrite'
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'old loader') 'An actually overwritten component is restored byte for byte'

$fixture=Fixture 'crash-recovery'
$result=Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save
$recordPath=Join-Path $result.Backup 'update.json'
$record=Get-Content $recordPath -Raw | ConvertFrom-Json; $record.State='Applying'; $record | ConvertTo-Json -Depth 6 | Set-Content $recordPath -Encoding UTF8
Repair-DrInterruptedUpdate $fixture.Game
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'old loader') 'Interrupted update is recovered before the next launch'

$fixture=Fixture 'external-change'
$result=Invoke-DrUpdate $fixture.Package $fixture.Game $fixture.Save
$recordPath=Join-Path $result.Backup 'update.json'
$record=Get-Content $recordPath -Raw | ConvertFrom-Json; $record.State='Applying'; $record | ConvertTo-Json -Depth 6 | Set-Content $recordPath -Encoding UTF8
[IO.File]::WriteAllText($fixture.Target,'external change')
Reject { Repair-DrInterruptedUpdate $fixture.Game } 'Recovery preserves unexpected external changes' 'externally changed'
Check ([IO.File]::ReadAllText($fixture.Target) -eq 'external change') 'Recovery never overwrites an unrelated external edit'
Write-Output "$script:Checks updater checks passed. Fixtures: $root"

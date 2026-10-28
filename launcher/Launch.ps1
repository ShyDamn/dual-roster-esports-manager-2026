param([string]$GameRoot, [switch]$UpdateOnly, [switch]$NoUi,
      [Parameter(Position=0)][string]$GameExe,
      [Parameter(ValueFromRemainingArguments=$true)][string[]]$GameArguments)
$ErrorActionPreference='Stop'
# Steam can inherit PSModulePath from PowerShell 7 or another launcher. Restore
# this host's built-in module path before resolving Windows PowerShell cmdlets.
$nativeModules=Join-Path $PSHOME 'Modules'
$env:PSModulePath=$nativeModules+[IO.Path]::PathSeparator+$env:PSModulePath
Import-Module (Join-Path $nativeModules 'Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1') -ErrorAction Stop
Import-Module (Join-Path $nativeModules 'Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1') -ErrorAction Stop
. (Join-Path $PSScriptRoot 'Update.Core.ps1')
if(![Environment]::Is64BitProcess) {
    $host64=Join-Path $env:WINDIR 'Sysnative\WindowsPowerShell\v1.0\powershell.exe'
    $arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden','-File',$PSCommandPath)
    if($GameRoot) { $arguments+=@('-GameRoot',$GameRoot) }
    if($UpdateOnly) { $arguments+='-UpdateOnly' }
    if($NoUi) { $arguments+='-NoUi' }
    if($GameExe) { $arguments+=@('-GameExe',$GameExe) }
    $arguments+=@($GameArguments)
    $start64=New-Object Diagnostics.ProcessStartInfo
    $start64.FileName=$host64; $start64.UseShellExecute=$false
    $start64.Arguments=($arguments | ForEach-Object { ConvertTo-DrArgument $_ }) -join ' '
    $process64=[Diagnostics.Process]::Start($start64)
    while(!$process64.WaitForExit(1000)) { }
    exit $process64.ExitCode
}
if(!$GameRoot) { $GameRoot=Split-Path $PSScriptRoot -Parent }
$GameRoot=[IO.Path]::GetFullPath($GameRoot)
$expectedExe=Get-DrInside $GameRoot 'EsportsManager.exe'
if(!$GameExe) { $GameExe=$expectedExe }
if([IO.Path]::GetFullPath($GameExe) -ne $expectedExe) { throw 'The launcher only starts its own Esports Manager installation.' }
$russian=[Globalization.CultureInfo]::CurrentUICulture.Name -like 'ru*'
$languageFile=Get-DrInside $GameRoot 'DualRoster\language.txt'
if(Test-Path -LiteralPath $languageFile) { $russian=(Get-Content -LiteralPath $languageFile -Raw).Trim() -eq 'ru' }
function Say([string]$Ru,[string]$En) { if($russian) { return $Ru } else { return $En } }
$form=$null; $label=$null; $progressBar=$null
if(!$NoUi) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $form=New-Object Windows.Forms.Form
    $form.Text='Dual Roster'; $form.Width=600; $form.Height=160
    $form.StartPosition='CenterScreen'; $form.FormBorderStyle='FixedDialog'; $form.MaximizeBox=$false; $form.MinimizeBox=$false
    $label=New-Object Windows.Forms.Label; $label.Left=18; $label.Top=16; $label.Width=550; $label.Height=50
    $label.Text=(Say 'Проверка обновления Workshop…' 'Checking the Workshop update…')
    $form.Controls.Add($label)
    $progressBar=New-Object Windows.Forms.ProgressBar; $progressBar.Left=18; $progressBar.Top=75; $progressBar.Width=550; $progressBar.Style='Marquee'
    $form.Controls.Add($progressBar); $form.Show(); [Windows.Forms.Application]::DoEvents()
}
$steamInitialized=$false
$logPath=Get-DrInside $GameRoot 'DualRoster\launcher.log'
New-Item -ItemType Directory -Path (Split-Path $logPath) -Force | Out-Null
function Log([string]$Message) {
    if((Test-Path -LiteralPath $logPath) -and (Get-Item -LiteralPath $logPath).Length -gt 262144) { Move-Item -LiteralPath $logPath -Destination ($logPath+'.previous') -Force }
    Add-Content -LiteralPath $logPath -Value ([DateTime]::UtcNow.ToString('o')+' '+$Message) -Encoding UTF8
}
function Pump {
    if($form) {
        [Windows.Forms.Application]::DoEvents()
        if($form.IsDisposed) { throw 'Update cancelled; the game was not started.' }
    }
}
try {
    if(@(Get-Process EsportsManager -ErrorAction SilentlyContinue | Where-Object { !$_.Path -or $_.Path -eq $expectedExe }).Count) { throw 'This game installation is already running.' }
    $currentHash=Get-DrHash (Get-DrInside $GameRoot 'GameAssembly.dll')
    $compatibility=Get-DrInside $GameRoot 'DualRoster\compatibility.json'
    $known=$false
    if(Test-Path -LiteralPath $compatibility) {
        $installed=Get-Content -LiteralPath $compatibility -Raw | ConvertFrom-Json
        $known=$installed.Schema -eq 1 -and $installed.GameHash -eq $currentHash
    }
    Repair-DrInterruptedUpdate $GameRoot
    if(-not ('DualRosterSteam' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'SteamBridge.cs') }
    $native=Get-DrInside $GameRoot 'EsportsManager_Data\Plugins\x86_64\steam_api64.dll'
    if([DualRosterSteam]::LoadLibrary($native) -eq [IntPtr]::Zero) { throw 'Cannot load the installed Steam API.' }
    $env:SteamAppId='2749950'
    $steamInitialized=[DualRosterSteam]::SteamAPI_Init()
    $package=$null
    if($steamInitialized) {
        $ugc=[DualRosterSteam]::SteamAPI_SteamUGC_v020()
        if($ugc -eq [IntPtr]::Zero) { throw 'Steam Workshop is unavailable.' }
        $item=[uint64]3819071177
        $state=[DualRosterSteam]::SteamAPI_ISteamUGC_GetItemState($ugc,$item)
        if($state -band 1) {
            $deadline=[DateTime]::UtcNow.AddSeconds(120)
            $requested=$false
            while(($state -band 4) -eq 0 -or ($state -band 56) -ne 0) {
                if(!$requested) { $requested=[DualRosterSteam]::SteamAPI_ISteamUGC_DownloadItem($ugc,$item,$true) }
                if([DateTime]::UtcNow -gt $deadline) { throw 'Steam download did not finish. Retry after Steam finishes downloading.' }
                Pump
                [DualRosterSteam]::SteamAPI_RunCallbacks()
                Start-Sleep -Milliseconds 250
                $state=[DualRosterSteam]::SteamAPI_ISteamUGC_GetItemState($ugc,$item)
                if(($state -band 1) -eq 0) { break }
            }
            if($state -band 1) { $package=[DualRosterSteam]::Folder($ugc,$item) }
        }
    }
    if($package) {
        if($label) { $label.Text=(Say 'Проверка совместимости и установка компонентов. При изменениях сохраняются копии файлов и карьеры.' 'Checking compatibility and installing components. Changed files and career data are backed up.') }
        Pump
        $saveRoot=Join-Path $env:USERPROFILE 'AppData\LocalLow\NeuronaGames\EsportsManager\Save'
        $result=Invoke-DrUpdate -Package $package -GameRoot $GameRoot -SaveRoot $saveRoot -Progress {
            param($done,$total)
            if($progressBar) { $progressBar.Style='Continuous'; $progressBar.Value=[int](100*$done/$total) }
            Pump
        }
        Log ("Ready: version="+$result.Version+' changed='+$result.Changed+' backup='+$result.Backup)
    } elseif(!$known) { throw 'No compatible installed components or subscribed Workshop package for this build.' }
    else { Log 'No subscribed package available. Compatible installed components retained; the in-game gate controls loading without the mod.' }
    if($steamInitialized) { [DualRosterSteam]::SteamAPI_Shutdown(); $steamInitialized=$false }
    if($form) { $form.Close(); $form.Dispose(); $form=$null }
    if($UpdateOnly) { Write-Output 'Dual Roster components verified and updated; game was not started.'; exit 0 }
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$GameExe; $start.WorkingDirectory=$GameRoot; $start.UseShellExecute=$false
    $start.Arguments=(@($GameArguments | ForEach-Object { ConvertTo-DrArgument $_ }) -join ' ')
    $process=[Diagnostics.Process]::Start($start)
    while(!$process.WaitForExit(1000)) { }
    exit $process.ExitCode
} catch {
    Log ('Failed: '+$_.Exception.Message)
    if($form) { $form.Close(); $form.Dispose(); $form=$null }
    if(!$NoUi) {
        [Windows.Forms.MessageBox]::Show((Say 'Игра не запущена: совместимое обновление пока недоступно или не прошло проверку. Дождитесь загрузки Steam и повторите запуск. Подробности: ' 'The game was not started: a compatible update is unavailable or failed verification. Let Steam finish downloading and retry. Details: ')+$logPath,'Dual Roster','OK','Warning') | Out-Null
    }
    Write-Error $_
    exit 1
} finally {
    if($steamInitialized) { [DualRosterSteam]::SteamAPI_Shutdown() }
    if($form) { $form.Dispose() }
}

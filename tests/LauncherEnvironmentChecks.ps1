param([Parameter(Mandatory=$true)][string]$GameRoot)
$ErrorActionPreference='Stop'
# Requires Steam and a compatible subscribed package. Never starts the game.
$launcher=Join-Path $PSScriptRoot '..\launcher\Launch.ps1'
$previous=$env:PSModulePath
try {
    $env:PSModulePath='C:\DualRoster-Test-NoModules'
    foreach($directory in @('System32','SysWOW64')) {
        $hostPath=Join-Path $env:WINDIR ($directory+'\WindowsPowerShell\v1.0\powershell.exe')
        & $hostPath -NoProfile -ExecutionPolicy Bypass -File $launcher -GameRoot $GameRoot -UpdateOnly -NoUi
        if($LASTEXITCODE -ne 0) { throw "Prelaunch environment check failed: $directory" }
        Write-Output "PASS prelaunch with unavailable inherited modules: $directory"
    }
} finally { $env:PSModulePath=$previous }

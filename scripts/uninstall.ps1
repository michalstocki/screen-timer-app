# Removes the Screen Timer service and tray app. Run from an elevated PowerShell.
#Requires -RunAsAdministrator
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'ScreenTimer'),
    # Also delete the code, configuration and break state.
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'ScreenTimerService'

if (Get-Service $serviceName -ErrorAction SilentlyContinue) {
    Stop-Service $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Write-Host 'Service removed.'
}

Get-Process ScreenTimer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }

if ($RemoveData) {
    $dataDir = Join-Path $env:ProgramData 'ScreenTimer'
    if (Test-Path $dataDir) { Remove-Item $dataDir -Recurse -Force }
    Write-Host 'Configuration and state removed.'
}
Write-Host 'Done.'

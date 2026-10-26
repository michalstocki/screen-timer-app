# Installs the Screen Timer service and tray app. Run from an elevated PowerShell:
#   .\scripts\build.ps1
#   .\scripts\install.ps1 -ExemptAccount parent1,parent2
#Requires -RunAsAdministrator
param(
    [string]$PublishDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\publish'),
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'ScreenTimer'),
    # Accounts that must never be limited (e.g. the parent's own account).
    [string[]]$ExemptAccount = @()
)

$ErrorActionPreference = 'Stop'
$serviceName = 'ScreenTimerService'
$dataDir = Join-Path $env:ProgramData 'ScreenTimer'
$configPath = Join-Path $dataDir 'config.json'

if (-not (Test-Path (Join-Path $PublishDir 'ScreenTimer.exe'))) {
    throw "No build found in $PublishDir. Run scripts\build.ps1 first."
}

# Stop a previous installation so its files can be replaced.
$existing = Get-Service $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host 'Stopping existing service...'
    Stop-Service $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
}
Get-Process ScreenTimer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Copying files to $InstallDir ..."
New-Item -ItemType Directory -Force $InstallDir | Out-Null
robocopy $PublishDir $InstallDir /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

# Data folder: SYSTEM and Administrators only, so the code hash cannot be read by a standard user.
New-Item -ItemType Directory -Force $dataDir | Out-Null
icacls $dataDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

if ($ExemptAccount.Count -gt 0) {
    $config = if (Test-Path $configPath) { Get-Content $configPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    $config | Add-Member -NotePropertyName ExemptAccounts -NotePropertyValue $ExemptAccount -Force
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding UTF8
    Write-Host "Exempt accounts: $($ExemptAccount -join ', ')"
}

$exe = Join-Path $InstallDir 'ScreenTimer.exe'
if (-not $existing) {
    New-Service -Name $serviceName -BinaryPathName "`"$exe`" --service" `
        -DisplayName 'Limit czasu przy komputerze' `
        -Description 'Liczy czas pracy przy komputerze i wymusza przerwy.' `
        -StartupType Automatic | Out-Null
}
# Restart automatically if the service is ever killed.
sc.exe failure $serviceName reset= 86400 actions= restart/2000/restart/2000/restart/2000 | Out-Null

Start-Service $serviceName
Write-Host "Service '$serviceName' is running."

$hasCode = (Test-Path $configPath) -and ((Get-Content $configPath -Raw) -match '"CodeHash"\s*:\s*"[^"]+"')
if (-not $hasCode) {
    Write-Host 'No lock code is set yet - opening settings so you can set one.'
    Start-Process $exe -ArgumentList '--settings'
}

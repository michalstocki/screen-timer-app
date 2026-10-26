# Installs the Screen Timer service and tray app. Run from an elevated PowerShell:
#   .\scripts\build.ps1
#   .\scripts\install.ps1 -LimitedAccount kid1,kid2
# Accounts can also be chosen later in the app's settings.
#Requires -RunAsAdministrator
param(
    # In a release ZIP the app sits in .\app next to this script; in the repo it is in dist\publish.
    [string]$PublishDir = $(if (Test-Path (Join-Path $PSScriptRoot 'app\ScreenTimer.exe')) { Join-Path $PSScriptRoot 'app' } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\publish' }),
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'ScreenTimer'),
    # Accounts the app runs for and whose time it counts (names such as kid1 or PC\kid1).
    [string[]]$LimitedAccount = @()
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

if ($LimitedAccount.Count -gt 0) {
    $accounts = @()
    foreach ($name in $LimitedAccount) {
        $nt = New-Object System.Security.Principal.NTAccount($name)
        $sid = $nt.Translate([System.Security.Principal.SecurityIdentifier]).Value
        $accounts += [pscustomobject]@{ Sid = $sid; Name = $nt.Value }
    }
    $config = if (Test-Path $configPath) { Get-Content $configPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    $config | Add-Member -NotePropertyName LimitedAccounts -NotePropertyValue $accounts -Force
    $config | ConvertTo-Json -Depth 5 | Set-Content $configPath -Encoding UTF8
    Write-Host "Limited accounts: $(($accounts | ForEach-Object Name) -join ', ')"
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

$text = if (Test-Path $configPath) { Get-Content $configPath -Raw } else { '' }
$hasCode = $text -match '"CodeHash"\s*:\s*"[^"]+"'
$hasAccounts = $text -match '"Sid"\s*:\s*"S-'
if (-not ($hasCode -and $hasAccounts)) {
    Write-Host 'Lock code or limited accounts are not set yet - opening settings.'
    Start-Process $exe -ArgumentList '--settings'
}

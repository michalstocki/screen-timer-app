# Builds release artifacts for every architecture into dist\release:
#   ScreenTimer-<version>-<rid>.msi   installer
#   ScreenTimer-<version>-<rid>.zip   app folder + PowerShell install scripts
param(
    [string]$Version = '0.0.1',
    [ValidateSet('win-x64', 'win-arm64', 'win-x86')]
    [string[]]$Runtime = @('win-x64', 'win-arm64', 'win-x86')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
if (Test-Path $localDotnet) { $env:DOTNET_ROOT = Join-Path $root '.dotnet' }

# WiX is installed into the project (not system-wide) on first use.
$wix = Join-Path $root '.tools\wix.exe'
if (-not (Test-Path $wix)) {
    & $dotnet tool install wix --version 5.0.2 --tool-path (Join-Path $root '.tools')
    if ($LASTEXITCODE -ne 0) { throw 'Installing WiX failed' }
}
$installerDir = Join-Path $root 'installer'
Push-Location $installerDir
try {
    & $wix extension add WixToolset.Util.wixext/5.0.2
    if ($LASTEXITCODE -ne 0) { throw 'Adding the WiX Util extension failed' }
}
finally { Pop-Location }

$release = Join-Path $root 'dist\release'
New-Item -ItemType Directory -Force $release | Out-Null

foreach ($rid in $Runtime) {
    Write-Host "=== $rid ==="
    $publish = Join-Path $root "dist\publish\$rid"
    $stage = Join-Path $root "dist\stage\$rid"
    foreach ($dir in @($publish, $stage)) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }

    & $dotnet publish (Join-Path $root 'src\ScreenTimer') -c Release -r $rid --self-contained true `
        -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    # ZIP: app\ plus the scripts, laid out the way install.ps1 expects.
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $publish (Join-Path $stage 'app') -Recurse
    Copy-Item (Join-Path $root 'scripts\install.ps1'), (Join-Path $root 'scripts\uninstall.ps1'), (Join-Path $root 'README.md') $stage
    $zip = Join-Path $release "ScreenTimer-$Version-$rid.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    tar.exe -a -c -f $zip -C $stage .
    if ($LASTEXITCODE -ne 0) { throw "Creating $zip failed" }

    # MSI
    $arch = $rid -replace '^win-', ''
    $msi = Join-Path $release "ScreenTimer-$Version-$rid.msi"
    Push-Location $installerDir
    try {
        & $wix build Package.wxs -arch $arch -d Version=$Version -d PublishDir=$publish -ext WixToolset.Util.wixext -pdbtype none -o $msi
        if ($LASTEXITCODE -ne 0) { throw "wix build failed for $rid" }
    }
    finally { Pop-Location }
}

Get-ChildItem $release | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }

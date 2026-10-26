# Builds a self-contained copy of the app into dist\publish (no .NET install needed on the target PC).
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = $(if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $root '.dotnet\dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$out = Join-Path $root 'dist\publish'

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

& $dotnet publish (Join-Path $root 'src\ScreenTimer') -c Release -r $Runtime --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Published ($Runtime) to $out"

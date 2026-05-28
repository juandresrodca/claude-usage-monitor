# End-to-end: build the portable EXE, then compile the Inno Setup installer.
#
# Output: ./dist/ClaudeUsageMonitor-<version>-Setup.exe
#
# Requirements:
#   - .NET 8 SDK (you have this if `dotnet --version` works)
#   - Inno Setup 6.x  →  winget install JRSoftware.InnoSetup
#                       or download from https://jrsoftware.org/isdl.php
#
# Usage:
#   pwsh build/build-installer.ps1                # uses csproj version
#   pwsh build/build-installer.ps1 -Version 0.2.0 # override
#   pwsh build/build-installer.ps1 -SkipPublish   # reuse last dist/ output

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$distDir  = Join-Path $repoRoot 'dist'
$issPath  = Join-Path $repoRoot 'installer\ClaudeUsageMonitor.iss'

if (-not $Version) {
    [xml]$csproj = Get-Content (Join-Path $repoRoot 'ClaudeUsageMonitor.csproj')
    $Version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
}

# 1. Publish portable EXE
if (-not $SkipPublish) {
    & (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Publish step failed.' }
}

$publishDir = Join-Path $distDir "ClaudeUsageMonitor-$Version-win-x64"
if (-not (Test-Path (Join-Path $publishDir 'ClaudeUsageMonitor.exe'))) {
    throw "Expected EXE not found at $publishDir. Did publish.ps1 succeed?"
}

# 2. Locate Inno Setup compiler
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $candidates = @(
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install with: winget install JRSoftware.InnoSetup"
}

Write-Host ""
Write-Host "==> Compiling installer with $iscc" -ForegroundColor Cyan

& $iscc `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DOutputDir=$distDir" `
    $issPath

if ($LASTEXITCODE -ne 0) { throw "iscc failed with exit code $LASTEXITCODE" }

$setupExe = Join-Path $distDir "ClaudeUsageMonitor-$Version-Setup.exe"
if (Test-Path $setupExe) {
    Write-Host ""
    Write-Host "Installer built." -ForegroundColor Green
    Write-Host "  Setup: $setupExe"
    Write-Host "  Size : $([Math]::Round((Get-Item $setupExe).Length / 1MB, 1)) MB"
}

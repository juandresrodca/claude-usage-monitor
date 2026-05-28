# Build a self-contained single-file Windows release.
#
# Output:  ./dist/ClaudeUsageMonitor-<version>-win-x64/
#   ClaudeUsageMonitor.exe        (single file, ~70 MB — includes .NET runtime)
#   WebView2Loader.dll            (native, sits next to the EXE)
#
# Usage:
#   pwsh build/publish.ps1                # default config + version from csproj
#   pwsh build/publish.ps1 -Version 0.2.0 # override version
#   pwsh build/publish.ps1 -Clean         # wipe dist/ first

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projPath = Join-Path $repoRoot 'ClaudeUsageMonitor.csproj'
$distDir  = Join-Path $repoRoot 'dist'

if (-not $Version) {
    [xml]$csproj = Get-Content $projPath
    $Version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
    if (-not $Version) { $Version = '0.0.0' }
}

$outDir = Join-Path $distDir "ClaudeUsageMonitor-$Version-win-x64"

if ($Clean -and (Test-Path $distDir)) {
    Write-Host "Cleaning $distDir" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $distDir
}

Write-Host "==> Publishing v$Version ($Configuration, win-x64) -> $outDir" -ForegroundColor Cyan

dotnet publish $projPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# pdb files end up next to the exe — drop them from the redistribution
Get-ChildItem $outDir -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done."  -ForegroundColor Green
Write-Host "  EXE : $(Join-Path $outDir 'ClaudeUsageMonitor.exe')"
Write-Host "  Size: $([Math]::Round((Get-Item (Join-Path $outDir 'ClaudeUsageMonitor.exe')).Length / 1MB, 1)) MB"

<#
.SYNOPSIS
    Publishes a self-contained win-x64 build of the PocketInk Host.
.PARAMETER OutputDir
    Where to publish to. Defaults to <repo root>\publish.
.NOTES
    This produces the .NET app and its dependencies. It does NOT bundle the FFmpeg shared
    libraries that screen mirroring (Phase 2) needs - those are a separate native runtime
    dependency the target machine must install (see scripts/install-dependencies.ps1 and
    docs/TROUBLESHOOTING.md). The drawing-tablet feature (Phase 1) works without them.
#>
param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "publish")
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $repoRoot "src\PocketInk.Host\PocketInk.Host.csproj"

Write-Host "Publishing PocketInk Host (Release, win-x64, self-contained) to $OutputDir..."
dotnet publish $hostProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $OutputDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Write-Host ""
Write-Host "Published to: $OutputDir"
Write-Host "Reminder: screen mirroring additionally needs the FFmpeg 8.1 shared build installed" -ForegroundColor Yellow
Write-Host "on the target machine (run scripts/install-dependencies.ps1, or see docs/TROUBLESHOOTING.md)." -ForegroundColor Yellow

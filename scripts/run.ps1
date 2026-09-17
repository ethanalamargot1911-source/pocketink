<#
.SYNOPSIS
    Builds (if needed) and launches the PocketInk Host.
.PARAMETER Configuration
    Build configuration to run: Debug (default) or Release.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot "src\PocketInk.Host\bin\$Configuration\net8.0-windows10.0.19041.0\PocketInk.Host.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Host executable not found - building first..."
    & (Join-Path $PSScriptRoot "build.ps1") -Configuration $Configuration
}

if (-not (Test-Path $exePath)) {
    throw "Build did not produce $exePath"
}

Write-Host "Starting PocketInk Host ($Configuration)..."
& $exePath

<#
.SYNOPSIS
    Restores and builds the PocketInk solution.
.PARAMETER Configuration
    Build configuration: Debug (default) or Release.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "PocketInk.sln"

Push-Location $repoRoot
try {
    Write-Host "Restoring $solution..."
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

    Write-Host "Building $solution ($Configuration)..."
    dotnet build $solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    Write-Host "Build succeeded."
}
finally {
    Pop-Location
}

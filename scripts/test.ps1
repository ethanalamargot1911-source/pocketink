<#
.SYNOPSIS
    Runs the full automated test suite: the xUnit tests in PocketInk.Tests, plus the Node.js
    golden-vector test that confirms the browser's JS binary-protocol encoder produces
    byte-identical output to the C# encoder.
.PARAMETER Configuration
    Build configuration to test: Debug (default) or Release.
#>
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot "PocketInk.sln"

Push-Location $repoRoot
try {
    Write-Host "Running xUnit tests ($Configuration)..."
    dotnet test $solution -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

    $goldenTest = Join-Path $repoRoot "tests\js\protocol.golden.test.js"
    if (Get-Command node -ErrorAction SilentlyContinue) {
        Write-Host "Running $goldenTest..."
        node $goldenTest
        if ($LASTEXITCODE -ne 0) { throw "protocol.golden.test.js failed with exit code $LASTEXITCODE." }
    }
    else {
        Write-Warning "Node.js not found on PATH - skipping tests/js/protocol.golden.test.js. Install Node.js to run it."
    }

    Write-Host "All tests passed."
}
finally {
    Pop-Location
}

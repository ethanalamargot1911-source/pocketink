<#
.SYNOPSIS
    Installs PocketInk's one native runtime dependency: the FFmpeg 8.1 shared build that Phase 2
    (WebRTC screen mirroring) needs. Phase 1 (drawing tablet) works without it.
.NOTES
    SIPSorceryMedia.FFmpeg 10.0.16 (the version this project pins) targets the FFmpeg 8.1 ABI
    specifically - a much newer or older FFmpeg shared build can fail to load. This was confirmed
    by testing 9.0.1 (failed to match) and 8.1 (worked) on the actual dev machine.
#>
$ErrorActionPreference = "Stop"

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Warning "winget was not found on this machine. Install the FFmpeg 8.1 SHARED build manually:"
    Write-Warning "  https://www.gyan.dev/ffmpeg/builds/ -> 'release full shared' -> version 8.1"
    Write-Warning "and ensure its bin\ directory (containing avcodec-62.dll etc.) is on PATH."
    exit 1
}

Write-Host "Installing FFmpeg (Shared) 8.1 via winget..."
winget install --id Gyan.FFmpeg.Shared --version 8.1 --accept-source-agreements --accept-package-agreements
$wingetExitCode = $LASTEXITCODE

Write-Host ""
if ($wingetExitCode -eq 0) {
    Write-Host "Installed. PocketInk will look for it on PATH first, then fall back to the winget" -ForegroundColor Green
    Write-Host "install location automatically - no PATH changes or reboot should be required." -ForegroundColor Green
}
else {
    Write-Warning "winget exited with code $wingetExitCode. If it reported the package is already"
    Write-Warning "installed at a different version, uninstall it first, then re-run this script."
}

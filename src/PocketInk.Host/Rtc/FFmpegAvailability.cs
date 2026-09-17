using System.IO;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.FFmpeg;

namespace PocketInk.Host.Rtc;

/// <summary>
/// Lazily initializes the native FFmpeg bindings the first time screen mirroring is requested,
/// rather than at app startup - Phase 1 (drawing tablet) must keep working even on a machine
/// that doesn't have FFmpeg's shared libraries installed (spec: phases are independently usable).
///
/// Tries the process PATH first, then falls back to the well-known winget install location:
/// a freshly-`winget install`-ed package isn't on PATH until the next login/Explorer restart,
/// so relying on PATH alone fails immediately after installation even though the DLLs exist.
/// </summary>
public static class FFmpegAvailability
{
    private static readonly object Lock = new();
    private static bool? _isAvailable;

    public static bool EnsureInitialised(ILogger logger)
    {
        lock (Lock)
        {
            if (_isAvailable is { } cached)
            {
                return cached;
            }

            foreach (var candidatePath in CandidatePaths())
            {
                try
                {
                    FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_WARNING, candidatePath, logger);
                    _isAvailable = true;
                    return true;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "FFmpeg initialisation attempt failed for path '{Path}'.", candidatePath ?? "(PATH)");
                }
            }

            logger.LogWarning("FFmpeg initialisation failed on every known location - screen mirroring will be unavailable. " +
                "Install the FFmpeg shared build (e.g. `winget install \"FFmpeg (Shared)\" --version 8.1`).");
            _isAvailable = false;
            return false;
        }
    }

    private static IEnumerable<string?> CandidatePaths()
    {
        yield return null; // Already discoverable on the process PATH.

        string wingetPackages;
        try
        {
            wingetPackages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        }
        catch (PlatformNotSupportedException)
        {
            yield break;
        }

        if (!Directory.Exists(wingetPackages))
        {
            yield break;
        }

        foreach (var packageDir in Directory.EnumerateDirectories(wingetPackages, "Gyan.FFmpeg.Shared*"))
        {
            foreach (var binDir in Directory.EnumerateDirectories(packageDir, "bin", SearchOption.AllDirectories))
            {
                yield return binDir;
            }
        }
    }
}

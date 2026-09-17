using Microsoft.Extensions.Logging.Abstractions;
using PocketInk.Core.Models;
using PocketInk.Host.Input;

namespace PocketInk.Host.Services;

/// <summary>Owns the singleton, cross-connection native-interop services shared by every WebSocket session.</summary>
public sealed class HostServices : IDisposable
{
    public AppSettings Settings { get; }
    public MonitorService Monitors { get; } = new();
    public SyntheticPenService PenService { get; }
    public SyntheticMouseService MouseService { get; }
    public PenWatchdog Watchdog { get; }
    public HotkeyInjector Hotkeys { get; }

    /// <summary>
    /// The pipeline for the currently connected phone, or null when no client is connected.
    /// Only one phone connects at a time by design, so this doubles as the connection-status
    /// flag and the source of real (non-fake) session metrics for the UI.
    /// </summary>
    public PenInputPipeline? ActiveSession { get; set; }

    public HostServices(AppSettings settings)
    {
        Settings = settings;
        PenService = new SyntheticPenService(NullLogger<SyntheticPenService>.Instance);
        PenService.Initialize();
        MouseService = new SyntheticMouseService(NullLogger<SyntheticMouseService>.Instance);
        Watchdog = new PenWatchdog(PenService, NullLogger<PenWatchdog>.Instance);
        Hotkeys = new HotkeyInjector(NullLogger<HotkeyInjector>.Instance);
    }

    public void Dispose()
    {
        Watchdog.Dispose();
        Hotkeys.Dispose();
        PenService.Dispose();
        MouseService.Dispose();
    }
}

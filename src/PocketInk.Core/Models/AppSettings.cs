namespace PocketInk.Core.Models;

public enum TabletAspectMode
{
    Stretch,
    MatchMonitorAspectRatio,
}

public enum VideoQuality
{
    Low,
    Balanced,
    High,
    Auto,
}

/// <summary>Persisted per-user settings (spec #89). Serialized to JSON under %LOCALAPPDATA%\PocketInk.</summary>
public sealed class AppSettings
{
    public string? SelectedMonitorDeviceId { get; set; }
    public PressureMode PressureMode { get; set; } = PressureMode.Constant;
    public double ConstantPressureFraction { get; set; } = 0.6;
    public SmoothingLevel Smoothing { get; set; } = SmoothingLevel.Low;
    public int WebServerPort { get; set; } = 17462;
    public string? PreferredNetworkAdapterId { get; set; }
    public VideoQuality VideoQuality { get; set; } = VideoQuality.Balanced;
    public int VideoFps { get; set; } = 60;
    public string VideoResolution { get; set; } = "Auto";
    public bool ToolbarVisible { get; set; } = true;
    public TabletAspectMode TabletAspectMode { get; set; } = TabletAspectMode.MatchMonitorAspectRatio;

    /// <summary>
    /// Supabase project URL and anon/publishable key, used only as a signaling rendezvous for
    /// Remote (cross-network) pairing - never for LAN mode. These are the project's public client
    /// credentials (safe under Supabase's RLS/Realtime authorization model), not secrets, but are
    /// still user-provided config rather than hardcoded so the app isn't tied to one project.
    /// </summary>
    public string? SupabaseUrl { get; set; }
    public string? SupabaseAnonKey { get; set; }

    /// <summary>
    /// Where the static web client is hosted for Remote mode (e.g. a GitHub Pages URL) - the phone
    /// isn't on the LAN, so it can't load the client from this PC the way Local mode's QR code
    /// does; the Remote QR instead points here, with the pairing code and Supabase config appended
    /// as query parameters.
    /// </summary>
    public string? RemoteClientBaseUrl { get; set; }

    /// <summary>
    /// Optional TURN/STUN relay for Remote mode's WebRTC connections, needed when two devices on
    /// different networks can't reach each other with plain ICE (the common case across real
    /// home/mobile NATs). Semicolon-separated so more than one URL (e.g. UDP and TCP/443 variants
    /// for restrictive firewalls) can share the same username/credential, matching how TURN
    /// providers like Metered's Open Relay hand out credentials. Local mode never needs this - same-LAN
    /// host candidates always work - so leaving these blank doesn't affect it.
    /// </summary>
    public string? TurnServerUrls { get; set; }
    public string? TurnUsername { get; set; }
    public string? TurnCredential { get; set; }
}

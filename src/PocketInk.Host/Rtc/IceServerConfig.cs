using PocketInk.Core.Models;
using SIPSorcery.Net;

namespace PocketInk.Host.Rtc;

/// <summary>
/// Builds the optional TURN/STUN <see cref="RTCConfiguration"/> shared by every host-side
/// <see cref="RTCPeerConnection"/> - both <see cref="ScreenMirrorSession"/>'s mirroring connection
/// and <see cref="Networking.SupabasePairingSession"/>'s bootstrap connection - from
/// <see cref="AppSettings"/>. Returns null when unconfigured, so callers fall back to the
/// zero-arg <c>new RTCPeerConnection()</c> that Local mode has always used unmodified.
/// </summary>
public static class IceServerConfig
{
    public static RTCConfiguration? Build(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.TurnServerUrls))
        {
            return null;
        }

        var iceServers = settings.TurnServerUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(url => new RTCIceServer
            {
                urls = url,
                username = settings.TurnUsername,
                credential = settings.TurnCredential,
            })
            .ToList();

        return new RTCConfiguration { iceServers = iceServers };
    }
}

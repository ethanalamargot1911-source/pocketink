using Newtonsoft.Json;
using Supabase.Realtime.Models;

namespace PocketInk.Host.Networking;

/// <summary>
/// The one broadcast payload shape relayed through a Supabase Realtime channel during Remote-mode
/// pairing: a WebRTC SDP offer/answer or a single trickled ICE candidate, discriminated by
/// <see cref="Kind"/> the same way <see cref="Core.Protocol.RtcSdpMessage"/> and
/// <see cref="Core.Protocol.RtcIceCandidateMessage"/> are discriminated by their own "type" field.
/// Uses Newtonsoft.Json attributes (not System.Text.Json) because that's what the Supabase.Realtime
/// package deserializes broadcast payloads with internally.
/// </summary>
public sealed class SignalRelayPayload
{
    [JsonProperty("kind")]
    public string Kind { get; set; } = "";

    [JsonProperty("sdp")]
    public string? Sdp { get; set; }

    [JsonProperty("sdpType")]
    public string? SdpType { get; set; }

    [JsonProperty("candidate")]
    public string? Candidate { get; set; }

    [JsonProperty("sdpMid")]
    public string? SdpMid { get; set; }

    [JsonProperty("sdpMLineIndex")]
    public ushort SdpMLineIndex { get; set; }

    [JsonProperty("usernameFragment")]
    public string? UsernameFragment { get; set; }
}

/// <summary>Required by Supabase.Realtime: a named subclass of the generic base, not the generic type itself.</summary>
public sealed class SignalRelayBroadcast : BaseBroadcast<SignalRelayPayload>
{
}

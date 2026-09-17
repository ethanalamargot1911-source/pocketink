using System.Text.Json.Serialization;

namespace PocketInk.Core.Protocol;

/// <summary>
/// JSON control-plane messages sent over the text WebSocket channel. Binary
/// frames carry only <see cref="InputPacket"/>; everything else (handshake,
/// heartbeat, hotkeys, session control) is a small JSON envelope with an
/// explicit "type" discriminator - never a raw deserialized arbitrary object
/// (spec #32).
/// </summary>
public static class ControlMessageType
{
    public const string Hello = "hello";
    public const string HelloAck = "hello-ack";
    public const string Heartbeat = "heartbeat";
    public const string HeartbeatAck = "heartbeat-ack";
    public const string Hotkey = "hotkey";
    public const string Session = "session";
    public const string Status = "status";
    public const string Error = "error";

    /// <summary>Client asks the host to start interactive screen mirroring (spec Phase 2).</summary>
    public const string StartMirror = "start-mirror";

    /// <summary>Client (or host) ends an active mirroring session.</summary>
    public const string StopMirror = "stop-mirror";

    /// <summary>WebRTC SDP offer or answer, carried as a JSON envelope over the same control channel.</summary>
    public const string RtcOffer = "rtc-offer";
    public const string RtcAnswer = "rtc-answer";

    /// <summary>A single trickled ICE candidate, from either peer.</summary>
    public const string RtcIceCandidate = "rtc-ice-candidate";

    /// <summary>Trackpad mode (spec: "optionally a trackpad"): a relative cursor move.</summary>
    public const string TrackpadMove = "trackpad-move";

    /// <summary>Trackpad mode: a tap (left button) or double-tap (right button).</summary>
    public const string TrackpadClick = "trackpad-click";

    /// <summary>
    /// Server→client only, sent periodically while mirroring is active: where the real OS cursor
    /// currently is, normalized to the target monitor - the native cursor is too small to see on a
    /// phone screen, so the client renders its own larger overlay at this position instead.
    /// </summary>
    public const string CursorPosition = "cursor-position";
}

public sealed class HelloMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.Hello;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = "";
}

public sealed class HelloAckMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.HelloAck;

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; }

    [JsonPropertyName("serverVersion")]
    public string ServerVersion { get; set; } = "";

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = "";
}

public sealed class HeartbeatMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.Heartbeat;

    [JsonPropertyName("clientTimeMs")]
    public long ClientTimeMs { get; set; }
}

public sealed class HeartbeatAckMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.HeartbeatAck;

    [JsonPropertyName("clientTimeMs")]
    public long ClientTimeMs { get; set; }

    [JsonPropertyName("serverTimeMs")]
    public long ServerTimeMs { get; set; }
}

public sealed class HotkeyMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.Hotkey;

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
}

public sealed class SessionMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.Session;

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";
}

public sealed class ErrorMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.Error;

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

/// <summary>A bare session-control envelope with no payload (start-mirror / stop-mirror).</summary>
public sealed class MirrorControlMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}

/// <summary>
/// A WebRTC SDP offer/answer. "sdpType" carries the actual SDP role ("offer"/"answer") so it
/// doesn't collide with our own envelope "type" discriminator (spec's binary/JSON split).
/// </summary>
public sealed class RtcSdpMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("sdpType")]
    public string SdpType { get; set; } = "";

    [JsonPropertyName("sdp")]
    public string Sdp { get; set; } = "";
}

/// <summary>A single trickled ICE candidate. Field names mirror the browser's native RTCIceCandidateInit.</summary>
public sealed class RtcIceCandidateMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.RtcIceCandidate;

    [JsonPropertyName("candidate")]
    public string Candidate { get; set; } = "";

    [JsonPropertyName("sdpMid")]
    public string? SdpMid { get; set; }

    [JsonPropertyName("sdpMLineIndex")]
    public ushort SdpMLineIndex { get; set; }

    [JsonPropertyName("usernameFragment")]
    public string? UsernameFragment { get; set; }
}

/// <summary>Trackpad mode: a relative cursor move in CSS pixels (already sensitivity-scaled client-side).</summary>
public sealed class TrackpadMoveMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.TrackpadMove;

    [JsonPropertyName("dx")]
    public double Dx { get; set; }

    [JsonPropertyName("dy")]
    public double Dy { get; set; }
}

/// <summary>Trackpad mode: a single tap-to-click.</summary>
public sealed class TrackpadClickMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.TrackpadClick;

    [JsonPropertyName("button")]
    public string Button { get; set; } = "left";
}

/// <summary>Where the real OS cursor is, normalized [0,1] to the target monitor.</summary>
public sealed class CursorPositionMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ControlMessageType.CursorPosition;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}

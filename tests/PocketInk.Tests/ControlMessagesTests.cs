using System.Text.Json;
using PocketInk.Core.Protocol;

namespace PocketInk.Tests;

/// <summary>
/// The WebRTC signaling envelopes (spec Phase 2) are hand-written JSON contracts shared with the
/// browser client - these confirm the wire field names match what a browser's native
/// RTCSessionDescriptionInit/RTCIceCandidateInit produce, since nothing else here re-validates that shape.
/// </summary>
public class ControlMessagesTests
{
    [Fact]
    public void RtcSdpMessage_SerializesWithBrowserCompatibleFieldNames()
    {
        var message = new RtcSdpMessage { Type = ControlMessageType.RtcOffer, SdpType = "offer", Sdp = "v=0\r\n..." };

        var json = JsonSerializer.Serialize(message);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal("rtc-offer", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("offer", doc.RootElement.GetProperty("sdpType").GetString());
        Assert.Equal("v=0\r\n...", doc.RootElement.GetProperty("sdp").GetString());
    }

    [Fact]
    public void RtcSdpMessage_RoundTripsThroughJson()
    {
        var original = new RtcSdpMessage { Type = ControlMessageType.RtcAnswer, SdpType = "answer", Sdp = "v=0\r\nfoo" };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<RtcSdpMessage>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Type, roundTripped!.Type);
        Assert.Equal(original.SdpType, roundTripped.SdpType);
        Assert.Equal(original.Sdp, roundTripped.Sdp);
    }

    [Fact]
    public void RtcIceCandidateMessage_SerializesWithBrowserCompatibleFieldNames()
    {
        var message = new RtcIceCandidateMessage
        {
            Candidate = "candidate:1 1 udp 2130706431 192.168.1.5 54321 typ host",
            SdpMid = "0",
            SdpMLineIndex = 0,
            UsernameFragment = "abcd",
        };

        var json = JsonSerializer.Serialize(message);
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(ControlMessageType.RtcIceCandidate, doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(message.Candidate, doc.RootElement.GetProperty("candidate").GetString());
        Assert.Equal("0", doc.RootElement.GetProperty("sdpMid").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("sdpMLineIndex").GetInt32());
        Assert.Equal("abcd", doc.RootElement.GetProperty("usernameFragment").GetString());
    }

    [Fact]
    public void MirrorControlMessage_RoundTripsType()
    {
        var message = new MirrorControlMessage { Type = ControlMessageType.StartMirror };

        var json = JsonSerializer.Serialize(message);
        var roundTripped = JsonSerializer.Deserialize<MirrorControlMessage>(json);

        Assert.Equal(ControlMessageType.StartMirror, roundTripped!.Type);
    }

    [Fact]
    public void TrackpadMoveMessage_RoundTripsDeltas()
    {
        var message = new TrackpadMoveMessage { Dx = -12.5, Dy = 3.25 };

        var json = JsonSerializer.Serialize(message);
        var roundTripped = JsonSerializer.Deserialize<TrackpadMoveMessage>(json);

        Assert.Equal(ControlMessageType.TrackpadMove, roundTripped!.Type);
        Assert.Equal(-12.5, roundTripped.Dx);
        Assert.Equal(3.25, roundTripped.Dy);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public void TrackpadClickMessage_RoundTripsButton(string button)
    {
        var message = new TrackpadClickMessage { Button = button };

        var json = JsonSerializer.Serialize(message);
        var roundTripped = JsonSerializer.Deserialize<TrackpadClickMessage>(json);

        Assert.Equal(ControlMessageType.TrackpadClick, roundTripped!.Type);
        Assert.Equal(button, roundTripped.Button);
    }

    [Fact]
    public void CursorPositionMessage_RoundTripsCoordinates()
    {
        var message = new CursorPositionMessage { X = 0.25, Y = 0.75 };

        var json = JsonSerializer.Serialize(message);
        var roundTripped = JsonSerializer.Deserialize<CursorPositionMessage>(json);

        Assert.Equal(ControlMessageType.CursorPosition, roundTripped!.Type);
        Assert.Equal(0.25, roundTripped.X);
        Assert.Equal(0.75, roundTripped.Y);
    }
}

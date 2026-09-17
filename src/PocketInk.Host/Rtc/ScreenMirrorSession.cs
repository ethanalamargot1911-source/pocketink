using System.Drawing;
using Microsoft.Extensions.Logging;
using PocketInk.Core.Coordinates;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using PocketInk.Host.Input;
using PocketInk.Host.Services;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;

namespace PocketInk.Host.Rtc;

/// <summary>
/// Drives one WebRTC screen-mirroring session for the connected phone (spec Phase 2): captures
/// the target monitor with FFmpeg's gdigrab-backed screen source, encodes H.264, and streams it
/// over a fresh <see cref="RTCPeerConnection"/>. A new instance is created per WebSocket
/// connection, mirroring the per-connection lifetime of <see cref="Input.PenInputPipeline"/> -
/// nothing here is shared across phones.
///
/// Also opens an unreliable/unordered RTCDataChannel (spec Stage N) that the browser uses as a
/// lower-latency path for high-frequency MOVE_CONTACT packets alongside the WebSocket; both feed
/// the same per-connection <see cref="PenInputPipeline"/>, whose sequence-number tracking already
/// tolerates packets interleaved from two transports.
/// </summary>
public sealed class ScreenMirrorSession : IAsyncDisposable
{
    private readonly HostServices _services;
    private readonly PenInputPipeline _pipeline;
    private readonly Func<object, Task> _sendJson;
    private readonly ILogger _logger;
    private RTCPeerConnection? _peerConnection;
    private FFmpegScreenSource? _screenSource;
    private MonitorInfo? _monitor;
    private System.Threading.Timer? _cursorTimer;
    private long _currentBitrateBps;
    private static readonly TimeSpan CursorBroadcastInterval = TimeSpan.FromMilliseconds(50);

    public ScreenMirrorSession(HostServices services, PenInputPipeline pipeline, Func<object, Task> sendJson, ILogger logger)
    {
        _services = services;
        _pipeline = pipeline;
        _sendJson = sendJson;
        _logger = logger;
    }

    public bool IsActive => _peerConnection is not null;

    public async Task StartAsync()
    {
        if (IsActive)
        {
            return;
        }

        if (!FFmpegAvailability.EnsureInitialised(_logger))
        {
            await _sendJson(new ErrorMessage { Message = "Screen mirroring unavailable: FFmpeg was not found on this PC." });
            return;
        }

        var monitor = _services.Monitors.GetByDeviceId(_services.Settings.SelectedMonitorDeviceId)
            ?? _services.Monitors.GetPrimaryOrFirst();
        _monitor = monitor;

        var screenSource = new FFmpegScreenSource("desktop", new Rectangle(monitor.Left, monitor.Top, monitor.Width, monitor.Height), 30);

        // gdigrab doesn't draw the cursor by default - without this the mirrored screen looks
        // like nothing is ever pointing at anything, which matters a lot once trackpad mode is
        // being used to control it. Confirmed empirically (captured a frame with the cursor
        // moved to a known position and visually verified it appears) before relying on it here.
        screenSource.InitialiseDecoder(new Dictionary<string, string> { ["draw_mouse"] = "1" });

        var iceConfig = IceServerConfig.Build(_services.Settings);
        var pc = iceConfig is not null ? new RTCPeerConnection(iceConfig) : new RTCPeerConnection();

        pc.onicecandidate += candidate =>
        {
            if (candidate is null)
            {
                return;
            }

            _ = _sendJson(new RtcIceCandidateMessage
            {
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex,
                UsernameFragment = candidate.usernameFragment,
            });
        };

        pc.OnVideoFormatsNegotiated += formats => screenSource.SetVideoSourceFormat(formats.First());

        pc.onconnectionstatechange += state =>
        {
            _logger.LogInformation("Mirror session connection state: {State}", state);
            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    _ = screenSource.StartVideo();
                    StartCursorBroadcast();
                    break;
                case RTCPeerConnectionState.closed or RTCPeerConnectionState.failed or RTCPeerConnectionState.disconnected:
                    _ = screenSource.CloseVideo();
                    StopCursorBroadcast();
                    break;
            }
        };

        pc.OnReceiveReport += (_, mediaType, report) => OnReceiveReport(mediaType, report, screenSource);

        screenSource.OnVideoSourceEncodedSample += pc.SendVideo;
        screenSource.OnVideoSourceError += err => _logger.LogWarning("Screen source error: {Error}", err);

        var track = new MediaStreamTrack(screenSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        _currentBitrateBps = VideoQualityPresets.InitialBitrateBps(_services.Settings.VideoQuality);
        screenSource.SetVideoEncoderBitrate(_currentBitrateBps, null, null, null);

        _peerConnection = pc;
        _screenSource = screenSource;

        // Unreliable/unordered: a lower-latency path for MOVE_CONTACT (spec Stage N). Sequence
        // numbers embedded in each InputPacket already let PenInputPipeline tolerate packets
        // interleaved out of order from this and the WebSocket's binary channel.
        var inputChannel = await pc.createDataChannel("input", new RTCDataChannelInit { ordered = false, maxRetransmits = 0 });
        inputChannel.onmessage += (_, _, data) => HandleDataChannelFrame(data);

        var offer = pc.createOffer(new RTCOfferOptions());
        await pc.setLocalDescription(offer);

        await _sendJson(new RtcSdpMessage { Type = ControlMessageType.RtcOffer, SdpType = "offer", Sdp = offer.sdp });
    }

    private void StartCursorBroadcast()
    {
        _cursorTimer ??= new System.Threading.Timer(_ => BroadcastCursorPosition(), null, TimeSpan.Zero, CursorBroadcastInterval);
    }

    private void StopCursorBroadcast()
    {
        _cursorTimer?.Dispose();
        _cursorTimer = null;
    }

    /// <summary>
    /// The native Windows cursor is too small to see on a phone screen when mirroring, so the
    /// client renders its own larger overlay - this is what feeds it the real cursor's position.
    /// </summary>
    private void BroadcastCursorPosition()
    {
        if (_monitor is null)
        {
            return;
        }

        var position = System.Windows.Forms.Cursor.Position;
        var (x, y) = CoordinateMapper.ScreenToNormalized(_monitor, position.X, position.Y);
        _ = _sendJson(new CursorPositionMessage { X = x, Y = y });
    }

    private void HandleDataChannelFrame(byte[] frame)
    {
        if (InputPacket.TryDecode(frame, out var packet, out _))
        {
            _pipeline.ProcessPacket(packet);
        }
    }

    private void OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket report, FFmpegVideoSource screenSource)
    {
        if (mediaType != SDPMediaTypesEnum.video || _services.Settings.VideoQuality != VideoQuality.Auto)
        {
            return; // Only auto-adapt the video stream, and only when the user asked for Auto.
        }

        var samples = report.ReceiverReport?.ReceptionReports;
        if (samples is null || samples.Count == 0)
        {
            return;
        }

        var averageFractionLost = (byte)samples.Average(s => s.FractionLost);
        var nextBitrate = BitrateAdaptationCalculator.ComputeNextBitrateBps(_currentBitrateBps, averageFractionLost);
        if (nextBitrate == _currentBitrateBps)
        {
            return;
        }

        _currentBitrateBps = nextBitrate;
        screenSource.SetVideoEncoderBitrate(_currentBitrateBps, null, null, null);
        _logger.LogInformation("Adapted mirror bitrate to {BitrateBps} bps (fraction lost {FractionLost}/256).", nextBitrate, averageFractionLost);
    }

    public Task HandleAnswerAsync(RtcSdpMessage answer)
    {
        _peerConnection?.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answer.Sdp });
        return Task.CompletedTask;
    }

    public Task HandleIceCandidateAsync(RtcIceCandidateMessage candidate)
    {
        if (string.IsNullOrEmpty(candidate.Candidate))
        {
            return Task.CompletedTask; // End-of-candidates marker - nothing to add.
        }

        _peerConnection?.addIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate.Candidate,
            sdpMid = candidate.SdpMid,
            sdpMLineIndex = candidate.SdpMLineIndex,
            usernameFragment = candidate.UsernameFragment,
        });
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        StopCursorBroadcast();

        if (_screenSource is not null)
        {
            await _screenSource.CloseVideo();
            _screenSource.Dispose();
            _screenSource = null;
        }

        _peerConnection?.close();
        _peerConnection = null;
    }
}

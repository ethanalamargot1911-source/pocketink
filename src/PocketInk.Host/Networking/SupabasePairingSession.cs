using Microsoft.Extensions.Logging;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using SIPSorcery.Net;
using Supabase;

namespace PocketInk.Host.Networking;

/// <summary>
/// Bootstraps one Remote-mode (cross-network) connection (spec's Supabase-relayed signaling plan,
/// Phase 3): the phone isn't on the LAN, so there is no WebSocket to reach it over for the initial
/// handshake. Instead, a short-lived Supabase Realtime Broadcast channel - named by a random pairing
/// code, exactly like the existing LAN <see cref="Security.PairingToken"/> - relays a WebRTC SDP
/// offer/answer and trickled ICE candidates until a dedicated "control" <see cref="RTCDataChannel"/>
/// opens between host and phone.
///
/// Once that data channel opens, Supabase's job is done: wrap it in a <see cref="DataChannelControlChannel"/>
/// and hand it to <see cref="PenSessionHandler"/> exactly like a LAN WebSocket connection - hello,
/// heartbeat, hotkeys, trackpad, and the *separate* peer connection <see cref="Rtc.ScreenMirrorSession"/>
/// negotiates for actual mirroring all flow over that data channel from then on. Supabase is never
/// touched again for the rest of that session.
/// </summary>
public sealed class SupabasePairingSession : IAsyncDisposable
{
    private const string ChannelNamePrefix = "pocketink-pair-";
    private const string SignalEventName = "signal";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger _logger;
    private Client? _client;
    private Supabase.Realtime.RealtimeChannel? _channel;
    private Supabase.Realtime.RealtimeBroadcast<SignalRelayBroadcast>? _broadcast;
    private RTCPeerConnection? _peerConnection;

    public string PairingCode { get; }

    public SupabasePairingSession(ILogger logger)
    {
        _logger = logger;
        // Reuses PairingToken purely for its vetted 128-bit-random-hex generation (.Value) - its
        // expiry tracking is irrelevant here (and the lifetime argument below unused beyond
        // satisfying the constructor) because this code's actual one-shot, time-bounded exposure
        // is enforced by ConnectAsync's own timeout and DisposeAsync tearing the channel down
        // right after, whether that attempt succeeded, failed, or timed out.
        PairingCode = PairingToken.CreateNew(TimeSpan.Zero, TimeProvider.System).Value;
    }

    /// <summary>
    /// Relays the offer/answer/ICE handshake over Supabase and returns once the phone's side of the
    /// bootstrap "control" data channel opens. The caller owns the returned channel's lifetime from
    /// there (typically via <see cref="PenSessionHandler.HandleAsync(IControlChannel, CancellationToken)"/>);
    /// this session keeps the underlying <see cref="RTCPeerConnection"/> alive until <see cref="DisposeAsync"/>.
    /// </summary>
    public async Task<RTCDataChannel> ConnectAsync(string supabaseUrl, string supabaseAnonKey, RTCConfiguration? iceConfig, CancellationToken ct)
    {
        var client = new Client(supabaseUrl, supabaseAnonKey, new SupabaseOptions { AutoConnectRealtime = true });
        await client.InitializeAsync();
        _client = client;

        var channel = client.Realtime.Channel(ChannelNamePrefix + PairingCode);
        _channel = channel;
        var broadcast = channel.Register<SignalRelayBroadcast>(broadcastSelf: false, broadcastAck: true);
        _broadcast = broadcast;

        var pc = iceConfig is not null ? new RTCPeerConnection(iceConfig) : new RTCPeerConnection();
        _peerConnection = pc;
        var dataChannelTcs = new TaskCompletionSource<RTCDataChannel>(TaskCreationOptions.RunContinuationsAsynchronously);

        pc.onicecandidate += candidate =>
        {
            if (candidate is null)
            {
                return;
            }

            _ = SendSignalAsync(new SignalRelayPayload
            {
                Kind = "ice-candidate",
                Candidate = candidate.candidate,
                SdpMid = candidate.sdpMid,
                SdpMLineIndex = candidate.sdpMLineIndex,
                UsernameFragment = candidate.usernameFragment,
            });
        };

        pc.onconnectionstatechange += state =>
            _logger.LogInformation("Remote pairing connection state: {State}", state);

        // The handler's own "broadcast" argument is the non-generic Supabase.Realtime.Models.BaseBroadcast
        // (its Payload is a raw Dictionary<string,object>) - Current() is what actually gives back our
        // strongly-typed SignalRelayPayload, confirmed against the real installed package via reflection.
        broadcast.AddBroadcastEventHandler((_, _) =>
        {
            var payload = broadcast.Current()?.Payload;
            if (payload is null)
            {
                return;
            }

            switch (payload.Kind)
            {
                case "answer":
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = payload.Sdp });
                    break;

                case "ice-candidate":
                    if (!string.IsNullOrEmpty(payload.Candidate))
                    {
                        pc.addIceCandidate(new RTCIceCandidateInit
                        {
                            candidate = payload.Candidate,
                            sdpMid = payload.SdpMid,
                            sdpMLineIndex = payload.SdpMLineIndex,
                            usernameFragment = payload.UsernameFragment,
                        });
                    }
                    break;
            }
        });

        var controlChannel = await pc.createDataChannel("control");
        controlChannel.onopen += () => dataChannelTcs.TrySetResult(controlChannel);

        await channel.Subscribe();

        var offer = pc.createOffer(new RTCOfferOptions());
        await pc.setLocalDescription(offer);
        await SendSignalAsync(new SignalRelayPayload { Kind = "offer", Sdp = offer.sdp, SdpType = "offer" });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(ConnectTimeout);
        await using var registration = timeoutCts.Token.Register(() => dataChannelTcs.TrySetCanceled(timeoutCts.Token));

        return await dataChannelTcs.Task;
    }

    private Task SendSignalAsync(SignalRelayPayload payload) =>
        _broadcast is null
            ? Task.CompletedTask
            : _broadcast.Send(SignalEventName, new SignalRelayBroadcast { Payload = payload });

    /// <summary>Safe to call more than once (e.g. once from an explicit cancel, once from <see cref="RemotePairingService"/>'s own cleanup) - a no-op after the first call.</summary>
    public ValueTask DisposeAsync()
    {
        if (_channel is not null && _client is not null)
        {
            _channel.Unsubscribe();
            _client.Realtime.Remove(_channel);
            _channel = null;
            _client = null;
        }

        _peerConnection?.close();
        _peerConnection = null;
        return ValueTask.CompletedTask;
    }
}

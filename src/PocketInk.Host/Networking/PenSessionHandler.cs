using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PocketInk.Core.Protocol;
using PocketInk.Host.Input;
using PocketInk.Host.Rtc;
using PocketInk.Host.Services;

namespace PocketInk.Host.Networking;

/// <summary>
/// Drives a single iPhone session end to end (spec #30-#32): the hello/heartbeat handshake,
/// binary InputPacket dispatch into the pen pipeline, the closed-set hotkey/session control
/// messages, and (Phase 2) WebRTC screen-mirroring signaling. Guarantees pipeline and
/// mirror-session cleanup no matter how the connection ends.
///
/// Transport-agnostic since the <see cref="IControlChannel"/> refactor: the same dispatch logic
/// serves both Local mode (the WebSocket overload below) and Remote mode (a WebRTC data channel,
/// once a phone connects via Supabase-relayed signaling instead of the LAN) - only how bytes
/// arrive differs, never what happens to them once they do.
///
/// There is exactly one <see cref="IControlChannel.TextReceived"/> subscription for the whole
/// connection lifetime, wired up before the channel starts pumping messages at all. An earlier
/// version subscribed a temporary "waiting for hello" handler only after starting the pump, which
/// raced a message that arrived (or was already buffered) before that subscription existed and
/// silently dropped it - caught by <c>WebSocketHandshakeTests.MismatchedProtocolVersion_IsRejected</c>
/// timing out instead of seeing the rejection. Never reintroduce a subscribe-after-start gap here.
/// </summary>
public sealed class PenSessionHandler
{
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(5);

    private readonly HostServices _services;

    public PenSessionHandler(HostServices services)
    {
        _services = services;
    }

    /// <summary>Per-connection state; a fresh instance is created for every socket so nothing leaks across reconnects.</summary>
    private sealed class ConnectionState
    {
        public required PenInputPipeline Pipeline { get; init; }
        public ScreenMirrorSession? Mirror { get; set; }
        public bool HandshakeDone { get; set; }
    }

    /// <summary>Local mode entry point: wraps the WebSocket in a channel and drives it the same way any other channel is driven.</summary>
    public Task HandleAsync(WebSocket socket, CancellationToken connectionClosed) =>
        HandleAsync(new WebSocketControlChannel(socket), connectionClosed);

    public async Task HandleAsync(IControlChannel channel, CancellationToken connectionClosed)
    {
        var state = new ConnectionState { Pipeline = new PenInputPipeline(_services) };
        var handshakeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribed once, before RunAsync starts pumping - see the class doc comment for why this
        // must never be split into "subscribe a hello-only handler, then swap it out later".
        channel.TextReceived += text => state.HandshakeDone
            ? HandleTextFrameAsync(text, channel, state, connectionClosed)
            : HandleHandshakeMessageAsync(text, channel, state, connectionClosed, handshakeTcs);
        channel.BinaryReceived += frame =>
        {
            HandleBinaryFrame(frame, state.Pipeline);
            return Task.CompletedTask;
        };

        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(connectionClosed);
        var pumpTask = channel.RunAsync(pumpCts.Token);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(connectionClosed);
            timeoutCts.CancelAfter(HelloTimeout);
            await using var registration = timeoutCts.Token.Register(() => handshakeTcs.TrySetCanceled(timeoutCts.Token));

            bool handshakeOk;
            try
            {
                handshakeOk = await handshakeTcs.Task;
            }
            catch (OperationCanceledException)
            {
                await channel.CloseAsync("Hello timeout.");
                return;
            }

            if (!handshakeOk)
            {
                return; // HandleHandshakeMessageAsync already closed the channel with the specific reason.
            }

            state.Pipeline.OnConnected();
            _services.ActiveSession = state.Pipeline;

            await pumpTask;
        }
        finally
        {
            // Whatever brought us here - a rejected handshake, a normal disconnect, an exception -
            // the caller (e.g. WebHostService's `using var socket = ...`) disposes the underlying
            // transport the moment this method returns. Disposing while RunAsync still has a
            // receive in flight aborts the connection at the transport level, which can truncate
            // data already handed to SendAsync moments earlier. Cancelling and awaiting the pump
            // here guarantees nothing is still in flight by the time we return control to the caller.
            pumpCts.Cancel();
            try
            {
                await pumpTask;
            }
            catch (OperationCanceledException)
            {
            }

            _services.ActiveSession = null;
            state.Pipeline.OnDisconnected();
            _services.MouseService.Cancel();
            if (state.Mirror is not null)
            {
                await state.Mirror.DisposeAsync();
            }
        }
    }

    private static async Task HandleHandshakeMessageAsync(string text, IControlChannel channel, ConnectionState state, CancellationToken connectionClosed, TaskCompletionSource<bool> handshakeTcs)
    {
        state.HandshakeDone = true;

        if (!TryReadType(text, out var type) || type != ControlMessageType.Hello)
        {
            await channel.CloseAsync("Expected hello message.");
            handshakeTcs.TrySetResult(false);
            return;
        }

        var hello = JsonSerializer.Deserialize<HelloMessage>(text);
        if (hello is null || hello.ProtocolVersion != ProtocolConstants.CurrentProtocolVersion)
        {
            await channel.SendJsonAsync(new ErrorMessage { Message = "Unsupported protocol version." }, connectionClosed);
            await channel.CloseAsync("Protocol version mismatch.");
            handshakeTcs.TrySetResult(false);
            return;
        }

        await channel.SendJsonAsync(new HelloAckMessage
        {
            ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
            ServerVersion = typeof(PenSessionHandler).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            MachineName = Environment.MachineName,
        }, connectionClosed);

        handshakeTcs.TrySetResult(true);
    }

    private static void HandleBinaryFrame(byte[] frame, PenInputPipeline pipeline)
    {
        if (InputPacket.TryDecode(frame, out var packet, out _))
        {
            pipeline.ProcessPacket(packet);
        }
    }

    private async Task HandleTextFrameAsync(string text, IControlChannel channel, ConnectionState state, CancellationToken ct)
    {
        if (!TryReadType(text, out var type))
        {
            return;
        }

        switch (type)
        {
            case ControlMessageType.Heartbeat:
                var heartbeat = JsonSerializer.Deserialize<HeartbeatMessage>(text);
                if (heartbeat is not null)
                {
                    await channel.SendJsonAsync(new HeartbeatAckMessage
                    {
                        ClientTimeMs = heartbeat.ClientTimeMs,
                        ServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    }, ct);
                }
                break;

            case ControlMessageType.Hotkey:
                var hotkey = JsonSerializer.Deserialize<HotkeyMessage>(text);
                if (hotkey is not null && Enum.TryParse<HotkeyAction>(hotkey.Action, ignoreCase: true, out var action))
                {
                    _services.Hotkeys.Execute(action);
                }
                break;

            case ControlMessageType.Session:
                var session = JsonSerializer.Deserialize<SessionMessage>(text);
                if (session?.Command == "stop")
                {
                    state.Pipeline.OnDisconnected();
                }
                break;

            case ControlMessageType.StartMirror:
                state.Mirror ??= new ScreenMirrorSession(_services, state.Pipeline, msg => channel.SendJsonAsync(msg, ct), NullLogger.Instance);
                await state.Mirror.StartAsync();
                break;

            case ControlMessageType.StopMirror:
                if (state.Mirror is not null)
                {
                    await state.Mirror.DisposeAsync();
                    state.Mirror = null;
                }
                break;

            case ControlMessageType.RtcAnswer:
                var answer = JsonSerializer.Deserialize<RtcSdpMessage>(text);
                if (answer is not null && state.Mirror is not null)
                {
                    await state.Mirror.HandleAnswerAsync(answer);
                }
                break;

            case ControlMessageType.RtcIceCandidate:
                var candidate = JsonSerializer.Deserialize<RtcIceCandidateMessage>(text);
                if (candidate is not null && state.Mirror is not null)
                {
                    await state.Mirror.HandleIceCandidateAsync(candidate);
                }
                break;

            case ControlMessageType.TrackpadMove:
                var move = JsonSerializer.Deserialize<TrackpadMoveMessage>(text);
                if (move is not null)
                {
                    _services.MouseService.Move((int)Math.Round(move.Dx), (int)Math.Round(move.Dy));
                }
                break;

            case ControlMessageType.TrackpadClick:
                var click = JsonSerializer.Deserialize<TrackpadClickMessage>(text);
                if (click is not null)
                {
                    _services.MouseService.Click(rightButton: click.Button == "right");
                }
                break;
        }
    }

    private static bool TryReadType(string json, out string type)
    {
        type = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
            {
                type = typeElement.GetString() ?? "";
                return true;
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }
}

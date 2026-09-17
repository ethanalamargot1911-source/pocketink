namespace PocketInk.Host.Networking;

/// <summary>
/// Transport-agnostic seam between <see cref="PenSessionHandler"/>'s message dispatch and the
/// underlying connection: a WebSocket for Local (same-LAN) mode, or a WebRTC RTCDataChannel for
/// Remote (Supabase-signaled) mode. Both carry the exact same JSON control messages
/// (<see cref="Core.Protocol.ControlMessageType"/>) and binary <see cref="Core.Protocol.InputPacket"/>
/// frames - only how bytes get from the phone to the host differs.
/// </summary>
public interface IControlChannel
{
    /// <summary>Raised for each complete incoming text (JSON) message.</summary>
    event Func<string, Task>? TextReceived;

    /// <summary>Raised for each complete incoming binary (InputPacket) frame.</summary>
    event Func<byte[], Task>? BinaryReceived;

    /// <summary>Raised once when the underlying transport closes, for any reason.</summary>
    event Action? Closed;

    Task SendJsonAsync<T>(T message, CancellationToken ct);

    Task CloseAsync(string reason);

    /// <summary>Pumps incoming messages, raising <see cref="TextReceived"/>/<see cref="BinaryReceived"/>,
    /// until the channel closes or <paramref name="ct"/> is cancelled.</summary>
    Task RunAsync(CancellationToken ct);
}

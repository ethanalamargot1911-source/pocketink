using System.Text;
using System.Text.Json;
using SIPSorcery.Net;

namespace PocketInk.Host.Networking;

/// <summary>
/// <see cref="IControlChannel"/> over a WebRTC <see cref="RTCDataChannel"/> - Remote mode, once a
/// phone connects via Supabase-relayed signaling instead of the LAN. Unlike
/// <see cref="WebSocketControlChannel"/> there is no receive loop to run: SIPSorcery's
/// <c>onmessage</c> already pushes each frame as it arrives, so <see cref="RunAsync"/> just waits
/// for the channel to close (or be cancelled) rather than pumping anything itself.
/// </summary>
public sealed class DataChannelControlChannel : IControlChannel
{
    private readonly RTCDataChannel _dataChannel;
    private readonly TaskCompletionSource _closedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Func<string, Task>? TextReceived;
    public event Func<byte[], Task>? BinaryReceived;
    public event Action? Closed;

    public DataChannelControlChannel(RTCDataChannel dataChannel)
    {
        _dataChannel = dataChannel;
        _dataChannel.onmessage += OnMessage;
        _dataChannel.onclose += OnClose;
    }

    private void OnMessage(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
    {
        // Our messages (small JSON control frames, fixed 28-byte InputPacket frames) are always
        // well under any realistic fragmentation threshold, so the _Partial/_Empty protocol
        // variants are treated as plain binary rather than specially reassembled or ignored.
        if (protocol == DataChannelPayloadProtocols.WebRTC_String)
        {
            _ = TextReceived?.Invoke(Encoding.UTF8.GetString(data));
        }
        else
        {
            _ = BinaryReceived?.Invoke(data);
        }
    }

    private void OnClose()
    {
        Closed?.Invoke();
        _closedTcs.TrySetResult();
    }

    public Task RunAsync(CancellationToken ct)
    {
        using var registration = ct.Register(() => _closedTcs.TrySetResult());
        return _closedTcs.Task;
    }

    public Task SendJsonAsync<T>(T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        _dataChannel.send(json);
        return Task.CompletedTask;
    }

    public Task CloseAsync(string reason)
    {
        _dataChannel.close();
        return Task.CompletedTask;
    }
}

using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PocketInk.Core.Protocol;

namespace PocketInk.Host.Networking;

/// <summary>
/// <see cref="IControlChannel"/> over the existing per-connection WebSocket - Local (same-LAN)
/// mode, unchanged behavior from before this abstraction existed. Reassembles multi-fragment
/// frames the same way the original inline receive loop did.
/// </summary>
public sealed class WebSocketControlChannel : IControlChannel
{
    private const int MaxFrameBytes = 64 * 1024;

    private readonly WebSocket _socket;

    public event Func<string, Task>? TextReceived;
    public event Func<byte[], Task>? BinaryReceived;
    public event Action? Closed;

    public WebSocketControlChannel(WebSocket socket)
    {
        _socket = socket;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[MaxFrameBytes];

            while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                using var messageStream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        // Safe to use the full (send-and-await-reply) close here specifically:
                        // this runs synchronously within the loop's own receive, so there is no
                        // concurrent ReceiveAsync in flight for it to collide with - unlike
                        // CloseAsync(string) below, which can be called while this loop's own
                        // receive is still pending.
                        await TryCloseAsync(WebSocketCloseStatus.NormalClosure, "Bye.");
                        return;
                    }

                    messageStream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage && messageStream.Length < MaxFrameBytes);

                if (result.MessageType == WebSocketMessageType.Binary && BinaryReceived is not null)
                {
                    await BinaryReceived(messageStream.ToArray());
                }
                else if (result.MessageType == WebSocketMessageType.Text
                    && messageStream.Length <= ProtocolConstants.MaxControlMessageBytes
                    && TextReceived is not null)
                {
                    await TextReceived(Encoding.UTF8.GetString(messageStream.ToArray()));
                }
            }
        }
        finally
        {
            Closed?.Invoke();
        }
    }

    public async Task SendJsonAsync<T>(T message, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <summary>
    /// Sends a close frame without waiting for the peer's reply. This can be (and typically is)
    /// called while <see cref="RunAsync"/>'s own receive loop still has a receive in flight (e.g.
    /// from <see cref="PenSessionHandler"/> rejecting a bad hello) - <c>WebSocket.CloseAsync</c>
    /// would itself try to receive until it sees the peer's close frame, and two concurrent
    /// receives on one WebSocket is invalid, corrupting the connection. Using the send-only
    /// close here and letting the loop's own receive naturally observe the resulting close frame
    /// (handled above) avoids that.
    /// </summary>
    public async Task CloseAsync(string reason)
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
    }

    private async Task TryCloseAsync(WebSocketCloseStatus status, string description)
    {
        try
        {
            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
            {
                await _socket.CloseAsync(status, description, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
        }
    }
}

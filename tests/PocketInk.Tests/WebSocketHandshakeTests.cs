using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using PocketInk.Host.Networking;
using PocketInk.Host.Security;
using PocketInk.Host.Services;

namespace PocketInk.Tests;

/// <summary>
/// Exercises the real Kestrel WebSocket endpoint end to end (spec's Stage F:
/// hello handshake, heartbeat). Deliberately never sends a binary InputPacket
/// frame: doing so would drive the real Win32 SyntheticPenService and inject
/// an actual synthetic pointer event on whatever machine runs the test suite,
/// which is unacceptable in an automated test (see PenWatchdog/SyntheticPenService
/// design notes). Text-only control messages are fully safe to automate.
/// </summary>
public class WebSocketHandshakeTests : IAsyncLifetime
{
    private readonly WebHostService _webHost = new();
    private readonly PairingService _pairing = new(TimeProvider.System);
    private HostServices? _hostServices;
    private HttpClient? _httpClient;

    public async Task InitializeAsync()
    {
        var settings = new AppSettings();
        _hostServices = new HostServices(settings);

        var webRoot = Path.Combine(Path.GetTempPath(), "PocketInkTestWwwRoot_" + Guid.NewGuid());
        Directory.CreateDirectory(webRoot);

        await _webHost.StartAsync(0, webRoot, _pairing, _hostServices);
        _pairing.Initialize($"http://127.0.0.1:{_webHost.Port}");
        _httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_webHost.Port}") };
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _webHost.DisposeAsync();
        _hostServices?.Dispose();
    }

    private async Task<string> PairAndGetSessionCookieAsync()
    {
        var token = _pairing.GetPairingUrl().Split("token=")[1];
        var response = await _httpClient!.PostAsJsonAsync("/api/pair", new { token });
        response.EnsureSuccessStatusCode();

        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        return setCookie.Split(';')[0]; // "pocketink_session=<value>"
    }

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var cookie = await PairAndGetSessionCookieAsync();
        var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Cookie", cookie);
        await client.ConnectAsync(new Uri($"ws://127.0.0.1:{_webHost.Port}/ws"), CancellationToken.None);
        return client;
    }

    private static async Task SendJsonAsync(ClientWebSocket socket, object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        return JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    [Fact]
    public async Task WithoutPairing_WsUpgradeIsRejected()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ws");
        request.Headers.Add("Connection", "Upgrade");
        request.Headers.Add("Upgrade", "websocket");
        request.Headers.Add("Sec-WebSocket-Version", "13");
        request.Headers.Add("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==");

        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hello_ReceivesHelloAckWithMatchingProtocolVersion()
    {
        using var socket = await ConnectAsync();

        await SendJsonAsync(socket, new
        {
            type = ControlMessageType.Hello,
            protocolVersion = ProtocolConstants.CurrentProtocolVersion,
            clientVersion = "test-1.0",
        });

        using var ack = await ReceiveJsonAsync(socket);

        Assert.Equal(ControlMessageType.HelloAck, ack.RootElement.GetProperty("type").GetString());
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, ack.RootElement.GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public async Task MismatchedProtocolVersion_IsRejected()
    {
        using var socket = await ConnectAsync();

        await SendJsonAsync(socket, new
        {
            type = ControlMessageType.Hello,
            protocolVersion = ProtocolConstants.CurrentProtocolVersion + 99,
            clientVersion = "test-1.0",
        });

        using var error = await ReceiveJsonAsync(socket);
        Assert.Equal(ControlMessageType.Error, error.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Heartbeat_ReceivesHeartbeatAckEchoingClientTime()
    {
        using var socket = await ConnectAsync();
        await SendJsonAsync(socket, new
        {
            type = ControlMessageType.Hello,
            protocolVersion = ProtocolConstants.CurrentProtocolVersion,
            clientVersion = "test-1.0",
        });
        await ReceiveJsonAsync(socket); // hello-ack

        await SendJsonAsync(socket, new { type = ControlMessageType.Heartbeat, clientTimeMs = 123456L });
        using var ack = await ReceiveJsonAsync(socket);

        Assert.Equal(ControlMessageType.HeartbeatAck, ack.RootElement.GetProperty("type").GetString());
        Assert.Equal(123456L, ack.RootElement.GetProperty("clientTimeMs").GetInt64());
        Assert.True(ack.RootElement.GetProperty("serverTimeMs").GetInt64() > 0);
    }
}

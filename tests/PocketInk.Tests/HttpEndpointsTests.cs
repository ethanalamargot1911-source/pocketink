using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using PocketInk.Host.Networking;
using PocketInk.Host.Security;
using PocketInk.Host.Services;

namespace PocketInk.Tests;

/// <summary>
/// Exercises the plain HTTP endpoints served by the real Kestrel host: status,
/// the SPA fallback that lets the QR code's "/pair?token=..." link actually
/// load the client shell, the pairing QR/status endpoints, and the tablet
/// aspect-ratio config the browser client needs for drawing-area matching
/// (spec #17, #18, #108, #109).
/// </summary>
public class HttpEndpointsTests : IAsyncLifetime
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
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<html>test-shell</html>");

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
        return setCookie.Split(';')[0];
    }

    [Fact]
    public async Task Status_ReturnsOkWithProtocolVersion()
    {
        var response = await _httpClient!.GetAsync("/api/status");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal(ProtocolConstants.CurrentProtocolVersion, body.GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public async Task PairRoute_ServesClientShellSoTheQrLinkWorks()
    {
        var response = await _httpClient!.GetAsync("/pair?token=whatever");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("test-shell", body);
    }

    [Fact]
    public async Task QrCode_ReturnsValidPng()
    {
        var response = await _httpClient!.GetAsync("/api/pair/qrcode");
        response.EnsureSuccessStatusCode();

        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes.Take(4));
    }

    [Fact]
    public async Task TabletConfig_WithoutPairing_IsUnauthorized()
    {
        var response = await _httpClient!.GetAsync("/api/tablet-config");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TabletConfig_WhenPaired_ReturnsPositiveMonitorAspectRatio()
    {
        var cookie = await PairAndGetSessionCookieAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tablet-config");
        request.Headers.Add("Cookie", cookie);

        var response = await _httpClient!.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("monitorAspectRatio").GetDouble() > 0);
        Assert.True(body.GetProperty("monitorWidth").GetInt32() > 0);
        Assert.True(body.GetProperty("monitorHeight").GetInt32() > 0);
    }
}

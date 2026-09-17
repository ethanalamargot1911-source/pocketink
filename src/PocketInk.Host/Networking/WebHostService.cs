using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketInk.Host.Security;
using PocketInk.Host.Services;

namespace PocketInk.Host.Networking;

/// <summary>
/// Owns the Kestrel web server that serves the iPhone web client (spec #17).
/// Tries the configured port first, then a small range of fallback ports if
/// it is already in use, since another instance or an unrelated app may be
/// bound to the default.
/// </summary>
public sealed class WebHostService : IAsyncDisposable
{
    private const int FallbackPortAttempts = 10;

    private WebApplication? _app;

    public int Port { get; private set; }

    public async Task StartAsync(int preferredPort, string webRootPath, PairingService pairingService, HostServices hostServices)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < FallbackPortAttempts; attempt++)
        {
            var candidatePort = preferredPort + attempt;
            try
            {
                var app = BuildApp(candidatePort, webRootPath, pairingService, hostServices);
                await app.StartAsync().ConfigureAwait(false);
                _app = app;
                Port = ResolveBoundPort(app, candidatePort);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
            }
            catch (SocketException ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"Could not bind to any port in range {preferredPort}-{preferredPort + FallbackPortAttempts - 1}.", lastError);
    }

    /// <summary>Reads back the actual bound port, since binding to port 0 (used by tests) lets the OS pick one.</summary>
    private static int ResolveBoundPort(WebApplication app, int fallback)
    {
        var addressFeature = app.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var address = addressFeature?.Addresses.FirstOrDefault();
        if (address is not null && Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return uri.Port;
        }

        return fallback;
    }

    private static WebApplication BuildApp(int port, string webRootPath, PairingService pairingService, HostServices hostServices)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            WebRootPath = webRootPath,
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(options =>
        {
            // Bind on all interfaces so the iPhone (a separate LAN device) can reach us,
            // not just loopback.
            options.Listen(IPAddress.Any, port);
        });

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();

        app.MapGet("/api/status", () => Results.Json(new
        {
            status = "ok",
            machineName = Environment.MachineName,
            protocolVersion = Core.Protocol.ProtocolConstants.CurrentProtocolVersion,
        }));

        app.MapGet("/api/tablet-config", (HttpContext ctx) =>
        {
            if (!pairingService.IsSessionValid(ctx.Request.Cookies[PairingService.SessionCookieName]))
            {
                return Results.Unauthorized();
            }

            var monitor = hostServices.Monitors.GetByDeviceId(hostServices.Settings.SelectedMonitorDeviceId)
                ?? hostServices.Monitors.GetPrimaryOrFirst();

            return Results.Json(new
            {
                matchMonitorAspectRatio = hostServices.Settings.TabletAspectMode == Core.Models.TabletAspectMode.MatchMonitorAspectRatio,
                monitorAspectRatio = monitor.AspectRatio,
                monitorWidth = monitor.Width,
                monitorHeight = monitor.Height,
            });
        });

        app.MapGet("/api/pair/qrcode", () => Results.File(pairingService.GeneratePairingQrPng(), "image/png"));

        app.MapGet("/api/pair/status", (HttpContext ctx) => Results.Json(new
        {
            paired = pairingService.IsSessionValid(ctx.Request.Cookies[PairingService.SessionCookieName]),
        }));

        app.MapPost("/api/pair", async (HttpContext ctx) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<PairRequest>();
            if (string.IsNullOrEmpty(body?.Token))
            {
                return Results.BadRequest();
            }

            var session = pairingService.TryPair(body.Token);
            if (session is null)
            {
                return Results.Unauthorized();
            }

            ctx.Response.Cookies.Append(PairingService.SessionCookieName, session, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                // LAN-only plain HTTP by design (spec #6) - no TLS, so the Secure flag would
                // block the cookie entirely rather than add protection.
                Secure = false,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
            });
            return Results.Ok(new { paired = true });
        });

        var sessionHandler = new PenSessionHandler(hostServices);
        app.MapGet("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Same-origin check (spec #32): a WebSocket upgrade always carries an Origin header,
            // and it must match the page that served the client - never trust a foreign origin.
            var origin = ctx.Request.Headers.Origin.ToString();
            var expectedOrigin = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
            if (!string.IsNullOrEmpty(origin) && !string.Equals(origin, expectedOrigin, StringComparison.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!pairingService.IsSessionValid(ctx.Request.Cookies[PairingService.SessionCookieName]))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await sessionHandler.HandleAsync(socket, ctx.RequestAborted);
        });

        // The QR code links to "/pair?token=...", which is not a static file - it's the same
        // single-page client shell, which reads the token from the URL itself (spec #18).
        app.MapFallbackToFile("index.html");

        return app;
    }

    private sealed class PairRequest
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }
}

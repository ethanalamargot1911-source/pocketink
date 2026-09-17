using Microsoft.Extensions.Logging;
using PocketInk.Host.Services;
using QRCoder;

namespace PocketInk.Host.Networking;

/// <summary>
/// Orchestrates one Remote-mode pairing attempt end to end: starts a <see cref="SupabasePairingSession"/>,
/// and once its bootstrap "control" data channel opens, hands off to <see cref="PenSessionHandler"/> -
/// from that point on a Remote session is driven identically to a LAN one, just over a different
/// <see cref="IControlChannel"/> implementation. Supabase is only ever touched during the handshake
/// this class drives; nothing here persists once <see cref="PenSessionHandler.HandleAsync(IControlChannel, CancellationToken)"/>
/// returns.
///
/// Only one Remote pairing attempt is tracked at a time, mirroring <see cref="HostServices.ActiveSession"/>'s
/// existing "one phone at a time" design - starting a new attempt implicitly abandons a prior
/// unfinished one.
/// </summary>
public sealed class RemotePairingService
{
    private readonly HostServices _services;
    private readonly ILogger _logger;
    private Attempt? _attempt;

    public RemotePairingService(HostServices services, ILogger logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>Ties one pairing attempt's session to the token that cancels it, and remembers
    /// whether that cancellation was an explicit user action (not worth a failure notification).</summary>
    private sealed class Attempt
    {
        public required SupabasePairingSession Session { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public bool CancelledByUser { get; set; }
    }

    /// <summary>Raised when a pairing attempt ends without a phone ever connecting (timeout, bad config, network error).</summary>
    public event Action<string>? PairingFailed;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_services.Settings.SupabaseUrl) &&
        !string.IsNullOrWhiteSpace(_services.Settings.SupabaseAnonKey) &&
        !string.IsNullOrWhiteSpace(_services.Settings.RemoteClientBaseUrl);

    /// <summary>
    /// Starts a new pairing rendezvous and returns its pairing code immediately, so the caller can
    /// display it (e.g. in a QR code, spec Phase 5) while the WebRTC handshake continues in the
    /// background. Throws if <see cref="IsConfigured"/> is false.
    /// </summary>
    public string StartPairing(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Remote pairing requires a Supabase URL, anon key, and remote client URL in settings.");
        }

        var session = new SupabasePairingSession(_logger);
        var attempt = new Attempt { Session = session, Cts = CancellationTokenSource.CreateLinkedTokenSource(ct) };
        _attempt = attempt;
        _ = RunAsync(attempt);
        return session.PairingCode;
    }

    /// <summary>
    /// The URL the Remote QR code encodes: the static client's own address (e.g. GitHub Pages),
    /// with the pairing code and Supabase config as query parameters - the phone isn't on this
    /// PC's network, so unlike Local mode's QR it can't just point back at this host.
    /// </summary>
    public string BuildPairingUrl(string pairingCode)
    {
        var baseUrl = _services.Settings.RemoteClientBaseUrl!.TrimEnd('/');
        var supabaseUrl = Uri.EscapeDataString(_services.Settings.SupabaseUrl!);
        var supabaseKey = Uri.EscapeDataString(_services.Settings.SupabaseAnonKey!);
        return $"{baseUrl}/?connect={pairingCode}&su={supabaseUrl}&sk={supabaseKey}";
    }

    public byte[] GeneratePairingQrPng(string pairingCode)
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(BuildPairingUrl(pairingCode), QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(20);
    }

    /// <summary>Abandons the in-progress pairing attempt, if any (e.g. the user cancels or closes the QR view).</summary>
    public async ValueTask CancelPairingAsync()
    {
        var attempt = _attempt;
        _attempt = null;
        if (attempt is null)
        {
            return;
        }

        attempt.CancelledByUser = true;
        attempt.Cts.Cancel();
        await attempt.Session.DisposeAsync();
    }

    private async Task RunAsync(Attempt attempt)
    {
        try
        {
            var controlChannel = await attempt.Session.ConnectAsync(
                _services.Settings.SupabaseUrl!, _services.Settings.SupabaseAnonKey!, attempt.Cts.Token);
            var sessionHandler = new PenSessionHandler(_services);
            await sessionHandler.HandleAsync(new DataChannelControlChannel(controlChannel), attempt.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!attempt.CancelledByUser)
            {
                PairingFailed?.Invoke("Timed out waiting for the phone to connect.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Remote pairing failed.");
            PairingFailed?.Invoke(ex.Message);
        }
        finally
        {
            await attempt.Session.DisposeAsync();
            if (ReferenceEquals(_attempt, attempt))
            {
                _attempt = null;
            }
            attempt.Cts.Dispose();
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using PocketInk.Core.Models;
using PocketInk.Core.Protocol;
using QRCoder;

namespace PocketInk.Host.Security;

/// <summary>
/// Owns the pairing lifecycle (spec #18): a short-lived, single-use pairing
/// token embedded in a QR code exchanges for a long-lived session cookie the
/// phone keeps until the user explicitly forgets it.
/// </summary>
public sealed class PairingService
{
    public const string SessionCookieName = "pocketink_session";

    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, byte> _activeSessions = new();
    private PairingToken _currentToken;

    public string BaseUrl { get; private set; } = "";

    public PairingService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _currentToken = PairingToken.CreateNew(ProtocolConstants.PairingTokenLifetime, _timeProvider);
    }

    public bool IsPaired => !_activeSessions.IsEmpty;

    public void Initialize(string baseUrl)
    {
        BaseUrl = baseUrl.TrimEnd('/');
    }

    public string GetPairingUrl() => $"{BaseUrl}/pair?token={_currentToken.Value}";

    /// <summary>
    /// Rotates the pairing token if it has expired since it was last displayed (spec #18: tokens
    /// are short-lived even if never scanned). Returns true if a new token was issued, so the
    /// caller knows to re-render the QR code. Safe to call frequently (e.g. from a UI refresh timer).
    /// </summary>
    public bool RefreshIfExpired()
    {
        if (!_currentToken.IsExpired(_timeProvider.GetUtcNow()))
        {
            return false;
        }

        RefreshPairingToken();
        return true;
    }

    public byte[] GeneratePairingQrPng()
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(GetPairingUrl(), QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(data);
        return pngQrCode.GetGraphic(20);
    }

    /// <summary>Consumes the current pairing token and returns a new session cookie value, or null if invalid.</summary>
    public string? TryPair(string presentedToken)
    {
        if (!_currentToken.TryConsume(presentedToken, _timeProvider.GetUtcNow()))
        {
            return null;
        }

        var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _activeSessions[session] = 0;
        return session;
    }

    public bool IsSessionValid(string? sessionCookieValue) =>
        sessionCookieValue is not null && _activeSessions.ContainsKey(sessionCookieValue);

    /// <summary>Revokes every paired session and issues a fresh pairing token (spec's "Forget paired iPhone").</summary>
    public void ForgetPairedDevice()
    {
        _activeSessions.Clear();
        RefreshPairingToken();
    }

    /// <summary>Invalidates the current QR code and issues a new pairing token (spec's "Refresh pairing QR").</summary>
    public void RefreshPairingToken()
    {
        _currentToken = PairingToken.CreateNew(ProtocolConstants.PairingTokenLifetime, _timeProvider);
    }
}

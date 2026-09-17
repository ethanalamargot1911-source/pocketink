using System.Security.Cryptography;

namespace PocketInk.Core.Models;

/// <summary>
/// A single-use, time-limited pairing token embedded in the QR code URL
/// (spec #18). Random (not sequential/timestamp-based), >=128 bits, expires
/// after a short window, and can only ever be consumed once.
/// </summary>
public sealed class PairingToken
{
    public string Value { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool IsUsed { get; private set; }

    public PairingToken(string value, DateTimeOffset expiresAtUtc)
    {
        Value = value;
        ExpiresAtUtc = expiresAtUtc;
    }

    public static PairingToken CreateNew(TimeSpan lifetime, TimeProvider timeProvider)
    {
        var bytes = RandomNumberGenerator.GetBytes(16); // 128 bits of entropy.
        var value = Convert.ToHexString(bytes).ToLowerInvariant();
        return new PairingToken(value, timeProvider.GetUtcNow() + lifetime);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;

    /// <summary>Atomically checks validity and marks the token used. Returns false for expired, already-used, or mismatched tokens.</summary>
    public bool TryConsume(string presentedValue, DateTimeOffset now)
    {
        if (IsUsed || IsExpired(now))
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(Value),
                System.Text.Encoding.UTF8.GetBytes(presentedValue)))
        {
            return false;
        }

        IsUsed = true;
        return true;
    }
}

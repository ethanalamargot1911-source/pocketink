namespace PocketInk.Core.Protocol;

public static class ProtocolConstants
{
    /// <summary>Bumped whenever the wire format (binary packet or JSON message shapes) changes incompatibly.</summary>
    public const int CurrentProtocolVersion = 1;

    /// <summary>Maximum accepted WebSocket text message size, in bytes. Rejects oversized control messages (spec #32, #122).</summary>
    public const int MaxControlMessageBytes = 8 * 1024;

    /// <summary>Pairing token lifetime (spec #18).</summary>
    public static readonly TimeSpan PairingTokenLifetime = TimeSpan.FromMinutes(2);

    /// <summary>If no valid input packet refreshes an active pen contact within this window, force a release (spec #36, #152).</summary>
    public static readonly TimeSpan PenWatchdogTimeout = TimeSpan.FromMilliseconds(500);
}

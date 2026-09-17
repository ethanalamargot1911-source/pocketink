namespace PocketInk.Core.Networking;

/// <summary>
/// Picks which LAN IPv4 address the pairing QR code should advertise (spec
/// #17). Pure selection logic so it can be unit tested without touching real
/// OS network interfaces.
/// </summary>
public static class LanAddressSelector
{
    /// <summary>
    /// Prefers the adapter matching <paramref name="preferredAdapterId"/> if it is
    /// still up and has an IPv4 address. Otherwise prefers the first up, non-loopback,
    /// non-link-local (169.254.x.x) candidate; falls back to a link-local address only
    /// if nothing better exists. Returns null if no adapter qualifies.
    /// </summary>
    public static string? SelectPreferredAddress(IReadOnlyList<NetworkAdapterInfo> adapters, string? preferredAdapterId)
    {
        var candidates = adapters
            .Where(a => a.IsUp && !a.IsLoopback && !string.IsNullOrEmpty(a.IPv4Address))
            .ToList();

        if (!string.IsNullOrEmpty(preferredAdapterId))
        {
            var preferred = candidates.FirstOrDefault(a => a.Id == preferredAdapterId);
            if (preferred is not null)
            {
                return preferred.IPv4Address;
            }
        }

        var nonLinkLocal = candidates.FirstOrDefault(a => !IsLinkLocal(a.IPv4Address!));
        if (nonLinkLocal is not null)
        {
            return nonLinkLocal.IPv4Address;
        }

        return candidates.FirstOrDefault()?.IPv4Address;
    }

    private static bool IsLinkLocal(string ipv4Address) => ipv4Address.StartsWith("169.254.", StringComparison.Ordinal);
}

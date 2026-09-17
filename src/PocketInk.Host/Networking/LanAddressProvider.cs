using System.Net.NetworkInformation;
using System.Net.Sockets;
using PocketInk.Core.Networking;

namespace PocketInk.Host.Networking;

/// <summary>Adapts real OS network interfaces into the pure <see cref="LanAddressSelector"/> logic.</summary>
public sealed class LanAddressProvider
{
    public string? GetPreferredAddress(string? preferredAdapterId)
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces()
            .Select(ToAdapterInfo)
            .ToList();

        return LanAddressSelector.SelectPreferredAddress(adapters, preferredAdapterId);
    }

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces().Select(ToAdapterInfo).ToList();

    private static NetworkAdapterInfo ToAdapterInfo(NetworkInterface nic)
    {
        var ipv4 = nic.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address.ToString();

        return new NetworkAdapterInfo
        {
            Id = nic.Id,
            DisplayName = nic.Name,
            IPv4Address = ipv4,
            IsUp = nic.OperationalStatus == OperationalStatus.Up,
            IsLoopback = nic.NetworkInterfaceType == NetworkInterfaceType.Loopback,
        };
    }
}

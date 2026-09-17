using PocketInk.Core.Networking;

namespace PocketInk.Tests;

public class LanAddressSelectorTests
{
    private static NetworkAdapterInfo Adapter(string id, string? ipv4, bool isUp = true, bool isLoopback = false) =>
        new()
        {
            Id = id,
            DisplayName = id,
            IPv4Address = ipv4,
            IsUp = isUp,
            IsLoopback = isLoopback,
        };

    [Fact]
    public void NoAdapters_ReturnsNull()
    {
        var result = LanAddressSelector.SelectPreferredAddress([], null);
        Assert.Null(result);
    }

    [Fact]
    public void IgnoresLoopbackAndDownAdapters()
    {
        var adapters = new[]
        {
            Adapter("loopback", "127.0.0.1", isLoopback: true),
            Adapter("down", "10.0.0.5", isUp: false),
            Adapter("wifi", "192.168.1.20"),
        };

        var result = LanAddressSelector.SelectPreferredAddress(adapters, null);

        Assert.Equal("192.168.1.20", result);
    }

    [Fact]
    public void PreferredAdapterId_TakesPriorityWhenUp()
    {
        var adapters = new[]
        {
            Adapter("wifi", "192.168.1.20"),
            Adapter("ethernet", "10.0.0.5"),
        };

        var result = LanAddressSelector.SelectPreferredAddress(adapters, "ethernet");

        Assert.Equal("10.0.0.5", result);
    }

    [Fact]
    public void PreferredAdapterId_IgnoredIfDownOrMissing_FallsBackToFirstCandidate()
    {
        var adapters = new[]
        {
            Adapter("ethernet", "10.0.0.5", isUp: false),
            Adapter("wifi", "192.168.1.20"),
        };

        var result = LanAddressSelector.SelectPreferredAddress(adapters, "ethernet");

        Assert.Equal("192.168.1.20", result);
    }

    [Fact]
    public void PrefersNonLinkLocalOverApipa()
    {
        var adapters = new[]
        {
            Adapter("apipa", "169.254.1.5"),
            Adapter("wifi", "192.168.1.20"),
        };

        var result = LanAddressSelector.SelectPreferredAddress(adapters, null);

        Assert.Equal("192.168.1.20", result);
    }

    [Fact]
    public void FallsBackToLinkLocalIfNothingElseAvailable()
    {
        var adapters = new[]
        {
            Adapter("apipa", "169.254.1.5"),
        };

        var result = LanAddressSelector.SelectPreferredAddress(adapters, null);

        Assert.Equal("169.254.1.5", result);
    }
}

namespace PocketInk.Core.Networking;

/// <summary>A minimal, testable snapshot of a network interface's relevant properties.</summary>
public sealed class NetworkAdapterInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string? IPv4Address { get; init; }
    public required bool IsUp { get; init; }
    public required bool IsLoopback { get; init; }
}

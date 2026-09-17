namespace PocketInk.Core.Coordinates;

/// <summary>
/// A single Windows display in virtual-screen (desktop) coordinates. Left/Top
/// may be negative for monitors positioned above/left of the primary display
/// (spec #37, #38).
/// </summary>
public sealed class MonitorInfo
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public required int Left { get; init; }
    public required int Top { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool IsPrimary { get; init; }
    public double DpiScale { get; init; } = 1.0;

    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public double AspectRatio => Height == 0 ? 1.0 : (double)Width / Height;
}

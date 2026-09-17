namespace PocketInk.Core.Models;

/// <summary>Fixed target bitrates per quality tier (spec Phase 2/N). Auto starts at Balanced and adapts from there.</summary>
public static class VideoQualityPresets
{
    public const long LowBitrateBps = 800_000;
    public const long BalancedBitrateBps = 2_500_000;
    public const long HighBitrateBps = 6_000_000;

    public static long InitialBitrateBps(VideoQuality quality) => quality switch
    {
        VideoQuality.Low => LowBitrateBps,
        VideoQuality.Balanced => BalancedBitrateBps,
        VideoQuality.High => HighBitrateBps,
        VideoQuality.Auto => BalancedBitrateBps,
        _ => BalancedBitrateBps,
    };
}

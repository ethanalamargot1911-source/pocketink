namespace PocketInk.Core.Models;

/// <summary>
/// Pure step function for "Auto" video quality (spec Phase 2/N): reacts to the real RTCP
/// receiver-report fraction-lost byte (0-255, where 256 represents 100% loss) by backing off
/// hard on real congestion, holding steady under mild loss, and ramping up slowly on a clean
/// link. Bounded to [LowBitrateBps, HighBitrateBps] so it never drifts outside sane limits.
/// </summary>
public static class BitrateAdaptationCalculator
{
    private const double HighLossThreshold = 0.10;
    private const double MildLossThreshold = 0.02;
    private const double BackOffFactor = 0.75;
    private const double RampUpFactor = 1.05;

    public static long ComputeNextBitrateBps(long currentBitrateBps, byte fractionLost)
    {
        var lossRatio = fractionLost / 256.0;

        long next = lossRatio switch
        {
            > HighLossThreshold => (long)(currentBitrateBps * BackOffFactor),
            > MildLossThreshold => currentBitrateBps,
            _ => (long)(currentBitrateBps * RampUpFactor),
        };

        return Math.Clamp(next, VideoQualityPresets.LowBitrateBps, VideoQualityPresets.HighBitrateBps);
    }
}

using PocketInk.Core.Models;

namespace PocketInk.Tests;

public class BitrateAdaptationCalculatorTests
{
    [Fact]
    public void HighLoss_BacksOffAggressively()
    {
        var next = BitrateAdaptationCalculator.ComputeNextBitrateBps(4_000_000, fractionLost: 100); // ~39% loss

        Assert.Equal((long)(4_000_000 * 0.75), next);
    }

    [Fact]
    public void MildLoss_HoldsSteady()
    {
        var next = BitrateAdaptationCalculator.ComputeNextBitrateBps(2_500_000, fractionLost: 15); // ~5.9% loss

        Assert.Equal(2_500_000, next);
    }

    [Fact]
    public void NoLoss_RampsUpGradually()
    {
        var next = BitrateAdaptationCalculator.ComputeNextBitrateBps(2_500_000, fractionLost: 0);

        Assert.Equal((long)(2_500_000 * 1.05), next);
    }

    [Fact]
    public void RampUp_NeverExceedsHighPreset()
    {
        var next = BitrateAdaptationCalculator.ComputeNextBitrateBps(VideoQualityPresets.HighBitrateBps, fractionLost: 0);

        Assert.Equal(VideoQualityPresets.HighBitrateBps, next);
    }

    [Fact]
    public void BackOff_NeverGoesBelowLowPreset()
    {
        var next = BitrateAdaptationCalculator.ComputeNextBitrateBps(VideoQualityPresets.LowBitrateBps, fractionLost: 255);

        Assert.Equal(VideoQualityPresets.LowBitrateBps, next);
    }

    [Theory]
    [InlineData(VideoQuality.Low, VideoQualityPresets.LowBitrateBps)]
    [InlineData(VideoQuality.Balanced, VideoQualityPresets.BalancedBitrateBps)]
    [InlineData(VideoQuality.High, VideoQualityPresets.HighBitrateBps)]
    [InlineData(VideoQuality.Auto, VideoQualityPresets.BalancedBitrateBps)]
    public void InitialBitrate_MatchesPresetForQuality(VideoQuality quality, long expectedBps)
    {
        Assert.Equal(expectedBps, VideoQualityPresets.InitialBitrateBps(quality));
    }
}

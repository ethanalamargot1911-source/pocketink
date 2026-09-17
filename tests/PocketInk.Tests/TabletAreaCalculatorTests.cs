using PocketInk.Core.Coordinates;

namespace PocketInk.Tests;

public class TabletAreaCalculatorTests
{
    [Fact]
    public void WidePhoneArea_LetterboxesLeftAndRight()
    {
        // Phone area wider than the 16:9 target -> pillarbox (offset on X).
        var area = TabletAreaCalculator.ComputeMatchedAspectArea(2000, 800, 16.0 / 9.0);
        Assert.Equal(800, area.Height);
        Assert.True(area.Left > 0);
        Assert.Equal(16.0 / 9.0, area.Width / area.Height, precision: 3);
    }

    [Fact]
    public void TallPhoneArea_LetterboxesTopAndBottom()
    {
        var area = TabletAreaCalculator.ComputeMatchedAspectArea(800, 2000, 16.0 / 9.0);
        Assert.Equal(800, area.Width);
        Assert.True(area.Top > 0);
    }

    [Fact]
    public void PointInsideMatchedArea_ReturnsNormalizedCoordinates()
    {
        var area = TabletAreaCalculator.ComputeMatchedAspectArea(1000, 1000, 1.0); // square area, square target
        var result = TabletAreaCalculator.PointToNormalized(area, area.Left + area.Width / 2, area.Top + area.Height / 2);
        Assert.NotNull(result);
        Assert.Equal(0.5, result!.Value.X, precision: 3);
        Assert.Equal(0.5, result.Value.Y, precision: 3);
    }

    [Fact]
    public void PointInInactivePadding_ReturnsNull()
    {
        var area = TabletAreaCalculator.ComputeMatchedAspectArea(2000, 800, 16.0 / 9.0);
        // Far left edge of the wide area should fall in the pillarbox padding.
        var result = TabletAreaCalculator.PointToNormalized(area, 1, 400);
        Assert.Null(result);
    }
}

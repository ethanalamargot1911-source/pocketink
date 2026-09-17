using PocketInk.Core.Coordinates;

namespace PocketInk.Tests;

public class VideoRectCalculatorTests
{
    [Fact]
    public void MatchingAspectRatio_FillsEntireElement()
    {
        var rect = VideoRectCalculator.ComputeContainRect(1920, 1080, 960, 540);
        Assert.Equal(0, rect.Left, precision: 3);
        Assert.Equal(0, rect.Top, precision: 3);
        Assert.Equal(960, rect.Width, precision: 3);
        Assert.Equal(540, rect.Height, precision: 3);
    }

    [Fact]
    public void PhoneRegionWiderThanVideoAspect_Pillarboxes()
    {
        // spec #66: 1920x1080 (16:9 ~= 1.78) display in an 844x390 (~2.16) phone drawing region.
        // The region is proportionally wider than the video, so the video fills the full
        // height and is pillarboxed (padded) on the left/right.
        var rect = VideoRectCalculator.ComputeContainRect(1920, 1080, 844, 390);
        Assert.Equal(390, rect.Height, precision: 3);
        Assert.True(rect.Width < 844);
        Assert.True(rect.Left > 0);
    }

    [Fact]
    public void ElementWiderThanVideo_Pillarboxes()
    {
        var rect = VideoRectCalculator.ComputeContainRect(1080, 1920, 1000, 1000);
        Assert.Equal(1000, rect.Height, precision: 3);
        Assert.True(rect.Width < 1000);
        Assert.True(rect.Left > 0);
    }

    [Fact]
    public void TouchOutsideContentRect_ReturnsNull()
    {
        var rect = VideoRectCalculator.ComputeContainRect(1920, 1080, 844, 390);
        var result = VideoRectCalculator.PointToNormalized(rect, 5, 5); // inside the top letterbox bar
        Assert.Null(result);
    }

    [Fact]
    public void TouchInsideContentRect_MapsToNormalizedCoordinates()
    {
        var rect = VideoRectCalculator.ComputeContainRect(1920, 1080, 844, 390);
        var center = VideoRectCalculator.PointToNormalized(rect, rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        Assert.NotNull(center);
        Assert.Equal(0.5, center!.Value.X, precision: 3);
        Assert.Equal(0.5, center.Value.Y, precision: 3);
    }
}

using PocketInk.Core.Coordinates;

namespace PocketInk.Tests;

public class CoordinateMapperTests
{
    public static IEnumerable<object[]> Monitors()
    {
        yield return new object[] { new MonitorInfo { DeviceId = "A", Name = "1920x1080 @ 0,0", Left = 0, Top = 0, Width = 1920, Height = 1080, IsPrimary = true } };
        yield return new object[] { new MonitorInfo { DeviceId = "B", Name = "2560x1440 @ 0,0", Left = 0, Top = 0, Width = 2560, Height = 1440, IsPrimary = true } };
        yield return new object[] { new MonitorInfo { DeviceId = "C", Name = "1920x1080 @ -1920,0", Left = -1920, Top = 0, Width = 1920, Height = 1080, IsPrimary = false } };
        yield return new object[] { new MonitorInfo { DeviceId = "D", Name = "1080x1920 vertical", Left = 1920, Top = 0, Width = 1080, Height = 1920, IsPrimary = false } };
        yield return new object[] { new MonitorInfo { DeviceId = "E", Name = "above primary", Left = 0, Top = -1080, Width = 1920, Height = 1080, IsPrimary = false } };
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void TopLeft_MapsToMonitorOrigin(MonitorInfo monitor)
    {
        var (x, y) = CoordinateMapper.NormalizedToScreen(monitor, 0.0, 0.0);
        Assert.Equal(monitor.Left, x);
        Assert.Equal(monitor.Top, y);
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void BottomRight_MapsInsideMonitorBounds(MonitorInfo monitor)
    {
        var (x, y) = CoordinateMapper.NormalizedToScreen(monitor, 1.0, 1.0);
        Assert.Equal(monitor.Right - 1, x);
        Assert.Equal(monitor.Bottom - 1, y);
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void Center_MapsNearMonitorCenter(MonitorInfo monitor)
    {
        var (x, y) = CoordinateMapper.NormalizedToScreen(monitor, 0.5, 0.5);
        Assert.InRange(x, monitor.Left + monitor.Width / 2 - 1, monitor.Left + monitor.Width / 2 + 1);
        Assert.InRange(y, monitor.Top + monitor.Height / 2 - 1, monitor.Top + monitor.Height / 2 + 1);
    }

    [Theory]
    [InlineData(-0.5, -0.5)]
    [InlineData(1.5, 1.5)]
    [InlineData(double.NaN, 0.5)]
    public void OutOfRangeInput_IsClamped(double nx, double ny)
    {
        var monitor = new MonitorInfo { DeviceId = "A", Name = "1920x1080", Left = 0, Top = 0, Width = 1920, Height = 1080, IsPrimary = true };
        var (x, y) = CoordinateMapper.NormalizedToScreen(monitor, double.IsNaN(nx) ? 0 : nx, ny);
        Assert.InRange(x, monitor.Left, monitor.Right - 1);
        Assert.InRange(y, monitor.Top, monitor.Bottom - 1);
    }

    [Fact]
    public void NegativeMonitorCoordinates_MapCorrectly()
    {
        var monitor = new MonitorInfo { DeviceId = "C", Name = "left of primary", Left = -1920, Top = 0, Width = 1920, Height = 1080, IsPrimary = false };
        var (x, y) = CoordinateMapper.NormalizedToScreen(monitor, 0.25, 0.5);
        Assert.Equal(-1920 + (int)Math.Round(0.25 * 1920), x);
        Assert.Equal((int)Math.Round(0.5 * 1080), y);
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void ScreenToNormalized_IsTheInverseOfNormalizedToScreen(MonitorInfo monitor)
    {
        var (screenX, screenY) = CoordinateMapper.NormalizedToScreen(monitor, 0.5, 0.5);
        var (x, y) = CoordinateMapper.ScreenToNormalized(monitor, screenX, screenY);

        Assert.InRange(x, 0.49, 0.51);
        Assert.InRange(y, 0.49, 0.51);
    }

    [Theory]
    [MemberData(nameof(Monitors))]
    public void ScreenToNormalized_MonitorOrigin_MapsToZeroZero(MonitorInfo monitor)
    {
        var (x, y) = CoordinateMapper.ScreenToNormalized(monitor, monitor.Left, monitor.Top);
        Assert.Equal(0.0, x, precision: 3);
        Assert.Equal(0.0, y, precision: 3);
    }

    [Fact]
    public void ScreenToNormalized_PointOutsideMonitor_ClampsToRange()
    {
        var monitor = new MonitorInfo { DeviceId = "A", Name = "1920x1080", Left = 0, Top = 0, Width = 1920, Height = 1080, IsPrimary = true };

        var (x, y) = CoordinateMapper.ScreenToNormalized(monitor, -500, 5000);

        Assert.InRange(x, 0.0, 1.0);
        Assert.InRange(y, 0.0, 1.0);
        Assert.Equal(0.0, x, precision: 3);
        Assert.Equal(1.0, y, precision: 3);
    }
}

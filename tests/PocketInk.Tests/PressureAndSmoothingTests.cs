using PocketInk.Core.Models;

namespace PocketInk.Tests;

public class PressureAndSmoothingTests
{
    [Theory]
    [InlineData(0.0, (ushort)0)]
    [InlineData(1.0, (ushort)1024)]
    [InlineData(0.6, (ushort)614)]
    public void PressureConverter_FromFraction_MapsToWindowsRange(double fraction, ushort expected)
    {
        Assert.Equal(expected, PressureConverter.FromFraction(fraction));
    }

    [Fact]
    public void PressureConverter_ClampsOutOfRangeFractions()
    {
        Assert.Equal(PressureConverter.MinPressure, PressureConverter.FromFraction(-5));
        Assert.Equal(PressureConverter.MaxPressure, PressureConverter.FromFraction(5));
    }

    [Fact]
    public void VelocityPressure_SlowStroke_ProducesHigherPressureThanFastStroke()
    {
        var slow = new VelocityPressureSimulator();
        var fast = new VelocityPressureSimulator();

        var slowPressure = slow.ComputePressure(0.01);
        var fastPressure = fast.ComputePressure(10.0);

        Assert.True(slowPressure > fastPressure);
    }

    [Fact]
    public void VelocityPressure_SmoothsSuddenJumps()
    {
        var sim = new VelocityPressureSimulator();
        var first = sim.ComputePressure(0.01); // slow -> high pressure
        var second = sim.ComputePressure(10.0); // sudden fast -> should not jump all the way immediately

        Assert.True(second > 0);
        Assert.True(Math.Abs(second - first) < (PressureConverter.MaxPressure - PressureConverter.MinPressure));
    }

    [Fact]
    public void PointSmoother_Off_ReturnsInputUnchanged()
    {
        var smoother = new PointSmoother(SmoothingLevel.Off);
        smoother.Smooth(0, 0);
        var (x, y) = smoother.Smooth(10, 10);
        Assert.Equal(10, x);
        Assert.Equal(10, y);
    }

    [Fact]
    public void PointSmoother_Low_DampensMovement()
    {
        var smoother = new PointSmoother(SmoothingLevel.Low);
        smoother.Smooth(0, 0);
        var (x, _) = smoother.Smooth(10, 0);
        Assert.True(x > 0 && x < 10);
    }
}

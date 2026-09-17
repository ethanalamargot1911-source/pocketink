using PocketInk.Core.Models;

namespace PocketInk.Tests;

public class PointerSpeedCalculatorTests
{
    [Fact]
    public void ZeroElapsedTime_ReturnsZero()
    {
        var speed = PointerSpeedCalculator.ComputeNormalizedSpeed(0.1, 0.1, 1000, 0.5, 0.5, 1000);
        Assert.Equal(0.0, speed);
    }

    [Fact]
    public void NegativeElapsedTime_ReturnsZero()
    {
        var speed = PointerSpeedCalculator.ComputeNormalizedSpeed(0.1, 0.1, 2000, 0.5, 0.5, 1000);
        Assert.Equal(0.0, speed);
    }

    [Fact]
    public void KnownDistanceAndTime_ComputesExpectedSpeed()
    {
        // Horizontal distance 0.5 over 0.5s -> 1.0 units/sec.
        var speed = PointerSpeedCalculator.ComputeNormalizedSpeed(0.0, 0.0, 0, 0.5, 0.0, 500);
        Assert.Equal(1.0, speed, precision: 6);
    }

    [Fact]
    public void DiagonalMovement_UsesEuclideanDistance()
    {
        // 3-4-5 triangle over 1 second -> speed 5.0.
        var speed = PointerSpeedCalculator.ComputeNormalizedSpeed(0.0, 0.0, 0, 3.0, 4.0, 1000);
        Assert.Equal(5.0, speed, precision: 6);
    }
}

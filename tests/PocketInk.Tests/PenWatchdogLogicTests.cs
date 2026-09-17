using PocketInk.Core.Safety;

namespace PocketInk.Tests;

public class PenWatchdogLogicTests
{
    [Fact]
    public void ActiveContact_PastTimeout_ForcesRelease()
    {
        var result = PenWatchdogLogic.ShouldForceRelease(
            contactActive: true,
            sinceLastInput: TimeSpan.FromMilliseconds(600),
            timeout: TimeSpan.FromMilliseconds(500));

        Assert.True(result);
    }

    [Fact]
    public void ActiveContact_WithinTimeout_DoesNotForceRelease()
    {
        var result = PenWatchdogLogic.ShouldForceRelease(
            contactActive: true,
            sinceLastInput: TimeSpan.FromMilliseconds(100),
            timeout: TimeSpan.FromMilliseconds(500));

        Assert.False(result);
    }

    [Fact]
    public void NoActiveContact_NeverForcesRelease()
    {
        var result = PenWatchdogLogic.ShouldForceRelease(
            contactActive: false,
            sinceLastInput: TimeSpan.FromSeconds(10),
            timeout: TimeSpan.FromMilliseconds(500));

        Assert.False(result);
    }

    [Fact]
    public void ExactlyAtTimeout_ForcesRelease()
    {
        var result = PenWatchdogLogic.ShouldForceRelease(
            contactActive: true,
            sinceLastInput: TimeSpan.FromMilliseconds(500),
            timeout: TimeSpan.FromMilliseconds(500));

        Assert.True(result);
    }
}

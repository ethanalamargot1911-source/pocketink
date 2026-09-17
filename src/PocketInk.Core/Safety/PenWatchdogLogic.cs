namespace PocketInk.Core.Safety;

/// <summary>
/// Pure decision logic for the pen watchdog, kept separate from the actual
/// timer so it is unit-testable without waiting on real wall-clock time
/// (spec #36, #152: "pen down, no further packets, timeout -> force-released").
/// </summary>
public static class PenWatchdogLogic
{
    public static bool ShouldForceRelease(bool contactActive, TimeSpan sinceLastInput, TimeSpan timeout)
        => contactActive && sinceLastInput >= timeout;
}

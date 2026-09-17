namespace PocketInk.Core.Coordinates;

/// <summary>
/// Maps normalized [0,1] tablet-surface coordinates to absolute Windows
/// virtual-screen coordinates for a target monitor (spec #38). Never assumes
/// the desktop origin is (0,0) - monitor.Left/Top may be negative.
/// </summary>
public static class CoordinateMapper
{
    public static (int X, int Y) NormalizedToScreen(MonitorInfo monitor, double normalizedX, double normalizedY)
    {
        var clampedX = Math.Clamp(normalizedX, 0.0, 1.0);
        var clampedY = Math.Clamp(normalizedY, 0.0, 1.0);

        var screenX = monitor.Left + clampedX * monitor.Width;
        var screenY = monitor.Top + clampedY * monitor.Height;

        // Guard the boundary pixel so we never inject a coordinate one pixel past the monitor edge.
        var x = (int)Math.Clamp(Math.Round(screenX), monitor.Left, monitor.Right - 1);
        var y = (int)Math.Clamp(Math.Round(screenY), monitor.Top, monitor.Bottom - 1);
        return (x, y);
    }

    /// <summary>
    /// The reverse of <see cref="NormalizedToScreen"/>: where the real OS cursor currently sits on
    /// the target monitor, as normalized [0,1] - used to mirror a visible cursor overlay onto the
    /// phone's screen (spec: the native cursor is too small to see there). A point outside the
    /// monitor (e.g. the cursor is actually on a different monitor) clamps into [0,1] rather than
    /// producing an out-of-range value, since the overlay has nowhere else sensible to show it.
    /// </summary>
    public static (double X, double Y) ScreenToNormalized(MonitorInfo monitor, int screenX, int screenY)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return (0.0, 0.0);
        }

        var x = (screenX - monitor.Left) / (double)monitor.Width;
        var y = (screenY - monitor.Top) / (double)monitor.Height;
        return (Math.Clamp(x, 0.0, 1.0), Math.Clamp(y, 0.0, 1.0));
    }
}

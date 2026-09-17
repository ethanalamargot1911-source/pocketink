namespace PocketInk.Core.Models;

/// <summary>Computes normalized-units-per-second speed between two timestamped points, for velocity-based pressure simulation (spec #28).</summary>
public static class PointerSpeedCalculator
{
    public static double ComputeNormalizedSpeed(double x1, double y1, ulong t1Ms, double x2, double y2, ulong t2Ms)
    {
        if (t2Ms <= t1Ms)
        {
            return 0.0;
        }

        var dx = x2 - x1;
        var dy = y2 - y1;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var dtSeconds = (t2Ms - t1Ms) / 1000.0;
        return distance / dtSeconds;
    }
}

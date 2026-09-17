namespace PocketInk.Core.Models;

/// <summary>
/// Single-pole exponential moving average smoother for pointer position.
/// Latency matters more than perfectly smooth paths, so this deliberately
/// avoids any multi-frame buffering (spec #41).
/// </summary>
public sealed class PointSmoother
{
    private readonly double _alpha;
    private double? _x;
    private double? _y;

    public PointSmoother(SmoothingLevel level)
    {
        _alpha = level switch
        {
            SmoothingLevel.Off => 1.0,
            SmoothingLevel.Low => 0.55,
            SmoothingLevel.Medium => 0.3,
            _ => 1.0,
        };
    }

    public (double X, double Y) Smooth(double x, double y)
    {
        if (_x is null || _y is null)
        {
            _x = x;
            _y = y;
            return (x, y);
        }

        _x += _alpha * (x - _x.Value);
        _y += _alpha * (y - _y.Value);
        return (_x.Value, _y.Value);
    }

    public void Reset()
    {
        _x = null;
        _y = null;
    }
}

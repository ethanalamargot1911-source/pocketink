namespace PocketInk.Core.Models;

/// <summary>
/// Optional simulated algorithm: slower strokes produce greater synthetic
/// pressure, faster strokes produce lower pressure, with smoothing to avoid
/// sudden jumps (spec #28). Cheap enough to run on every pointer event.
/// </summary>
public sealed class VelocityPressureSimulator
{
    private readonly double _minSpeed;
    private readonly double _maxSpeed;
    private readonly ushort _minPressure;
    private readonly ushort _maxPressure;
    private readonly double _smoothingAlpha;
    private double? _smoothed;

    public VelocityPressureSimulator(
        double minSpeedNormalizedPerSecond = 0.05,
        double maxSpeedNormalizedPerSecond = 3.0,
        ushort minPressure = 200,
        ushort maxPressure = 900,
        double smoothingAlpha = 0.35)
    {
        _minSpeed = minSpeedNormalizedPerSecond;
        _maxSpeed = maxSpeedNormalizedPerSecond;
        _minPressure = minPressure;
        _maxPressure = maxPressure;
        _smoothingAlpha = smoothingAlpha;
    }

    /// <summary>Computes smoothed pressure from the current stroke speed, expressed in normalized units per second.</summary>
    public ushort ComputePressure(double speedNormalizedPerSecond)
    {
        var range = _maxSpeed - _minSpeed;
        var t = range <= 0 ? 0.0 : Math.Clamp((speedNormalizedPerSecond - _minSpeed) / range, 0.0, 1.0);

        // Slow (t=0) -> max pressure. Fast (t=1) -> min pressure.
        var target = _maxPressure - t * (_maxPressure - _minPressure);

        _smoothed = _smoothed is null ? target : _smoothed + _smoothingAlpha * (target - _smoothed.Value);
        return (ushort)Math.Clamp(Math.Round(_smoothed.Value), PressureConverter.MinPressure, PressureConverter.MaxPressure);
    }

    public void Reset() => _smoothed = null;
}

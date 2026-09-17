namespace PocketInk.Core.Models;

/// <summary>Converts fractional pressure (0..1) into the Windows synthetic pointer pressure range (spec #27).</summary>
public static class PressureConverter
{
    public const ushort MinPressure = 0;
    public const ushort MaxPressure = 1024;
    public const ushort DefaultPressure = 614; // Constant 60%

    public static ushort FromFraction(double fraction)
    {
        var clamped = Math.Clamp(fraction, 0.0, 1.0);
        return (ushort)Math.Round(clamped * MaxPressure);
    }
}

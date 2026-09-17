namespace PocketInk.Core.Models;

/// <summary>Simulated pressure is never presented to the user as genuine hardware pressure sensitivity (spec #27).</summary>
public enum PressureMode
{
    Constant,
    VelocitySimulated,
    Disabled,
}

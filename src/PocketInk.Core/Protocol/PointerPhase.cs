namespace PocketInk.Core.Protocol;

/// <summary>
/// Explicit wire values for pointer contact phase. Do not rely on browser
/// PointerEvent enum internals - these values are the cross-platform contract
/// between the Safari client and the Windows host (spec #30).
/// </summary>
public enum PointerPhase : byte
{
    Move = 0,
    Down = 1,
    MoveContact = 2,
    Up = 3,
    Cancel = 4,
}

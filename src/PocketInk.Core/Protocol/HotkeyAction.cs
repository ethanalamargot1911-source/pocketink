namespace PocketInk.Core.Protocol;

/// <summary>
/// Closed set of remote-triggerable actions. The web client can never send
/// arbitrary keycodes or text (spec #45, #105) - only these named actions.
/// </summary>
public enum HotkeyAction
{
    Undo,
    Redo,
    Space,
    Escape,
    RightClick,
    CtrlDown,
    CtrlUp,
    ShiftDown,
    ShiftUp,
    AltDown,
    AltUp,
}

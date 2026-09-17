using System.Runtime.InteropServices;

namespace PocketInk.Host.Interop;

// SendInput structures for keyboard hotkey injection and mouse-click fallback
// (spec #45, #50). Layout mirrors <winuser.h> INPUT/KEYBDINPUT/MOUSEINPUT.

internal enum INPUT_TYPE : uint
{
    INPUT_MOUSE = 0,
    INPUT_KEYBOARD = 1,
    INPUT_HARDWARE = 2,
}

[Flags]
internal enum KEYEVENTF : uint
{
    NONE = 0x0000,
    EXTENDEDKEY = 0x0001,
    KEYUP = 0x0002,
    UNICODE = 0x0004,
    SCANCODE = 0x0008,
}

[Flags]
internal enum MOUSEEVENTF : uint
{
    MOVE = 0x0001,
    LEFTDOWN = 0x0002,
    LEFTUP = 0x0004,
    RIGHTDOWN = 0x0008,
    RIGHTUP = 0x0010,
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct INPUT_UNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type; // INPUT_TYPE
    public INPUT_UNION u;
}

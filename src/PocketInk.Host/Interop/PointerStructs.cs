using System.Runtime.InteropServices;

namespace PocketInk.Host.Interop;

// Struct layouts mirror the C definitions exactly (field order, sizes) so the
// CLR's default sequential-layout alignment rules reproduce the same padding
// as the native compiler on x64 (spec #33). Do not reorder fields.

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTER_INFO
{
    public int pointerType; // POINTER_INPUT_TYPE
    public uint pointerId;
    public uint frameId;
    public uint pointerFlags; // POINTER_FLAGS
    public IntPtr sourceDevice; // HANDLE
    public IntPtr hwndTarget; // HWND
    public POINT ptPixelLocation;
    public POINT ptHimetricLocation;
    public POINT ptPixelLocationRaw;
    public POINT ptHimetricLocationRaw;
    public uint dwTime;
    public uint historyCount;
    public int InputData;
    public uint dwKeyStates;
    public ulong PerformanceCount;
    public int ButtonChangeType; // POINTER_BUTTON_CHANGE_TYPE
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINTER_PEN_INFO
{
    public POINTER_INFO pointerInfo;
    public uint penFlags; // PEN_FLAGS
    public uint penMask; // PEN_MASK
    public uint pressure;
    public uint rotation;
    public int tiltX;
    public int tiltY;
}

/// <summary>
/// Native POINTER_TYPE_INFO is a tagged union (type + touchInfo/penInfo).
/// We only ever populate the pen variant; sequential layout naturally
/// reproduces the same 4-byte padding the C compiler inserts before the
/// union so penInfo lands at the correct 8-byte-aligned offset.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINTER_TYPE_INFO
{
    public int type; // POINTER_INPUT_TYPE
    public POINTER_PEN_INFO penInfo;
}

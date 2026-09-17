using System.Runtime.InteropServices;

namespace PocketInk.Host.Interop;

/// <summary>
/// Minimal, hand-reviewed P/Invoke surface. Classic DllImport is used (rather
/// than LibraryImport) specifically for the synthetic pointer device
/// functions because SafeHandle-returning/accepting marshaling for them is
/// the most mature, well-tested path and this is the single most safety
/// critical native call in the application (spec #33, #137, #138).
/// </summary>
internal static partial class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern SyntheticPointerDeviceHandle CreateSyntheticPointerDevice(
        POINTER_INPUT_TYPE pointerType,
        uint maxCount,
        POINTER_FEEDBACK_MODE mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InjectSyntheticPointerInput(
        SyntheticPointerDeviceHandle device,
        ref POINTER_TYPE_INFO pointerInfo,
        uint count);

    /// <summary>Raw destroy used only by <see cref="SyntheticPointerDeviceHandle"/>.ReleaseHandle - never call directly.</summary>
    [DllImport("user32.dll", EntryPoint = "DestroySyntheticPointerDevice")]
    internal static extern void DestroySyntheticPointerDeviceRaw(IntPtr device);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint SendInput(uint numInputs, [MarshalAs(UnmanagedType.LPArray)] INPUT[] inputs, int sizeOfInputStructure);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int MDT_EFFECTIVE_DPI = 0;
}

using Microsoft.Win32.SafeHandles;

namespace PocketInk.Host.Interop;

/// <summary>
/// Owns an HSYNTHETICPOINTERDEVICE. Guarantees DestroySyntheticPointerDevice
/// is called exactly once, even on exceptions or GC finalization, so the
/// device can never leak (spec #138).
/// </summary>
internal sealed class SyntheticPointerDeviceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SyntheticPointerDeviceHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.DestroySyntheticPointerDeviceRaw(handle);
        return true;
    }
}

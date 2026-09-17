using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PocketInk.Host.Interop;

namespace PocketInk.Host.Input;

/// <summary>
/// Owns exactly one synthetic PT_PEN contact and centralizes its lifecycle
/// (spec #34). Every public method is safe to call from any thread; a single
/// active contact invariant is enforced under a lock so a stray watchdog
/// release can never race with an in-flight move.
/// </summary>
public sealed class SyntheticPenService : IDisposable
{
    private const uint PenPointerId = 1;

    private readonly ILogger<SyntheticPenService> _logger;
    private readonly object _lock = new();
    private SyntheticPointerDeviceHandle? _device;
    private uint _frameId;
    private bool _contactActive;
    private int _lastX;
    private int _lastY;

    public SyntheticPenService(ILogger<SyntheticPenService> logger)
    {
        _logger = logger;
    }

    public bool IsContactActive
    {
        get { lock (_lock) { return _contactActive; } }
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_device is { IsInvalid: false })
            {
                return;
            }

            var device = NativeMethods.CreateSyntheticPointerDevice(POINTER_INPUT_TYPE.PT_PEN, maxCount: 1, POINTER_FEEDBACK_MODE.POINTER_FEEDBACK_DEFAULT);
            if (device.IsInvalid)
            {
                var win32Error = Marshal.GetLastWin32Error();
                device.Dispose();
                throw new InvalidOperationException(
                    $"CreateSyntheticPointerDevice failed (Win32 error {win32Error}). " +
                    "PocketInk could not create a virtual pen device.");
            }

            _device = device;
            _logger.LogInformation("Synthetic PT_PEN device created.");
        }
    }

    public void PenDown(int screenX, int screenY, ushort pressure)
    {
        lock (_lock)
        {
            Inject(screenX, screenY, pressure,
                POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT | POINTER_FLAGS.DOWN | POINTER_FLAGS.PRIMARY);
            _contactActive = true;
        }
    }

    public void PenMove(int screenX, int screenY, ushort pressure)
    {
        lock (_lock)
        {
            if (!_contactActive)
            {
                // A move without a preceding down (e.g. after a dropped packet) starts a new contact
                // rather than silently discarding real ink from the user.
                Inject(screenX, screenY, pressure,
                    POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT | POINTER_FLAGS.DOWN | POINTER_FLAGS.PRIMARY);
                _contactActive = true;
                return;
            }

            Inject(screenX, screenY, pressure,
                POINTER_FLAGS.INRANGE | POINTER_FLAGS.INCONTACT | POINTER_FLAGS.UPDATE | POINTER_FLAGS.PRIMARY);
        }
    }

    public void PenUp(int screenX, int screenY)
    {
        lock (_lock)
        {
            if (!_contactActive)
            {
                return;
            }

            Inject(screenX, screenY, 0, POINTER_FLAGS.UP | POINTER_FLAGS.PRIMARY);
            _contactActive = false;
        }
    }

    /// <summary>Force-releases the current contact at its last known position. Safe to call even if nothing is active (spec #36).</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            if (!_contactActive)
            {
                return;
            }

            Inject(_lastX, _lastY, 0, POINTER_FLAGS.UP | POINTER_FLAGS.CANCELED | POINTER_FLAGS.PRIMARY);
            _contactActive = false;
            _logger.LogWarning("Synthetic pen contact force-canceled at ({X},{Y}).", _lastX, _lastY);
        }
    }

    private void Inject(int screenX, int screenY, ushort pressure, POINTER_FLAGS flags)
    {
        if (_device is null || _device.IsInvalid)
        {
            throw new InvalidOperationException("SyntheticPenService.Initialize() must be called before injecting pen input.");
        }

        _lastX = screenX;
        _lastY = screenY;
        _frameId++;

        var pointerInfo = new POINTER_INFO
        {
            pointerType = (int)POINTER_INPUT_TYPE.PT_PEN,
            pointerId = PenPointerId,
            frameId = _frameId,
            pointerFlags = (uint)flags,
            ptPixelLocation = new POINT { X = screenX, Y = screenY },
        };

        var penInfo = new POINTER_PEN_INFO
        {
            pointerInfo = pointerInfo,
            penFlags = (uint)PEN_FLAGS.NONE,
            penMask = (uint)PEN_MASK.PRESSURE,
            pressure = pressure,
        };

        var typeInfo = new POINTER_TYPE_INFO
        {
            type = (int)POINTER_INPUT_TYPE.PT_PEN,
            penInfo = penInfo,
        };

        if (!NativeMethods.InjectSyntheticPointerInput(_device, ref typeInfo, 1))
        {
            var win32Error = Marshal.GetLastWin32Error();
            _logger.LogError("InjectSyntheticPointerInput failed (Win32 error {Error}) at ({X},{Y}), flags={Flags}.", win32Error, screenX, screenY, flags);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_contactActive)
            {
                Cancel();
            }

            _device?.Dispose();
            _device = null;
        }
    }
}

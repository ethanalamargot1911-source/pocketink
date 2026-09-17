using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PocketInk.Host.Interop;

namespace PocketInk.Host.Input;

/// <summary>
/// Trackpad mode (spec: "optionally a trackpad"): relative cursor movement and clicks via
/// SendInput, distinct from the tablet's absolute PT_PEN positioning in
/// <see cref="SyntheticPenService"/>. Tracks which button is currently held so it can always be
/// force-released on disconnect, mirroring <see cref="HotkeyInjector"/>'s modifier-release policy -
/// the same "never leave a stuck input" requirement applies to a stuck mouse button.
/// </summary>
public sealed class SyntheticMouseService : IDisposable
{
    private readonly ILogger<SyntheticMouseService> _logger;
    private readonly object _lock = new();
    private bool _leftDown;
    private bool _rightDown;

    public SyntheticMouseService(ILogger<SyntheticMouseService> logger)
    {
        _logger = logger;
    }

    /// <summary>Relative move, exactly like a real trackpad - never warps to an absolute position.</summary>
    public void Move(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        Send(new MOUSEINPUT { dx = dx, dy = dy, dwFlags = (uint)MOUSEEVENTF.MOVE });
    }

    public void Click(bool rightButton)
    {
        lock (_lock)
        {
            SetButtonDown(rightButton, down: true);
            SetButtonDown(rightButton, down: false);
        }
    }

    private void SetButtonDown(bool rightButton, bool down)
    {
        ref var heldFlag = ref (rightButton ? ref _rightDown : ref _leftDown);
        if (heldFlag == down)
        {
            return;
        }

        var flag = (rightButton, down) switch
        {
            (true, true) => MOUSEEVENTF.RIGHTDOWN,
            (true, false) => MOUSEEVENTF.RIGHTUP,
            (false, true) => MOUSEEVENTF.LEFTDOWN,
            (false, false) => MOUSEEVENTF.LEFTUP,
        };

        Send(new MOUSEINPUT { dwFlags = (uint)flag });
        heldFlag = down;
    }

    /// <summary>Force-releases any held button. Safe to call even if nothing is held (spec's "never leave a stuck input").</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            SetButtonDown(rightButton: false, down: false);
            SetButtonDown(rightButton: true, down: false);
        }
    }

    private void Send(MOUSEINPUT mouseInput)
    {
        var input = new INPUT { type = (uint)INPUT_TYPE.INPUT_MOUSE, u = new INPUT_UNION { mi = mouseInput } };
        var sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent != 1)
        {
            _logger.LogError("SendInput (mouse) failed (Win32 error {Error}).", Marshal.GetLastWin32Error());
        }
    }

    public void Dispose() => Cancel();
}

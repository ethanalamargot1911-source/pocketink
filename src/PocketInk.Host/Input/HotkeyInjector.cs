using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PocketInk.Core.Protocol;
using PocketInk.Host.Interop;

namespace PocketInk.Host.Input;

/// <summary>
/// Injects only a closed set of named actions via SendInput - never arbitrary
/// keycodes or text from the untrusted web client (spec #45, #105). Tracks
/// which modifiers we are holding down so they can always be force-released
/// on disconnect (spec #46, #153).
/// </summary>
public sealed class HotkeyInjector : IDisposable
{
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkMenu = 0x12; // Alt
    private const ushort VkSpace = 0x20;
    private const ushort VkEscape = 0x1B;
    private const ushort VkZ = 0x5A;
    private const ushort VkY = 0x59;

    private readonly ILogger<HotkeyInjector> _logger;
    private readonly object _lock = new();
    private readonly HashSet<ushort> _heldModifiers = new();

    public HotkeyInjector(ILogger<HotkeyInjector> logger)
    {
        _logger = logger;
    }

    public IReadOnlyCollection<HotkeyAction> HeldModifiers
    {
        get
        {
            lock (_lock)
            {
                return _heldModifiers.Select(VkToModifierAction).ToArray();
            }
        }
    }

    public void Execute(HotkeyAction action)
    {
        lock (_lock)
        {
            switch (action)
            {
                case HotkeyAction.Undo: PressCombo(VkControl, VkZ); break;
                case HotkeyAction.Redo: PressCombo(VkControl, VkY); break;
                case HotkeyAction.Space: TapKey(VkSpace); break;
                case HotkeyAction.Escape: TapKey(VkEscape); break;
                case HotkeyAction.RightClick: ClickRightMouseButton(); break;
                case HotkeyAction.CtrlDown: HoldModifier(VkControl); break;
                case HotkeyAction.CtrlUp: ReleaseModifier(VkControl); break;
                case HotkeyAction.ShiftDown: HoldModifier(VkShift); break;
                case HotkeyAction.ShiftUp: ReleaseModifier(VkShift); break;
                case HotkeyAction.AltDown: HoldModifier(VkMenu); break;
                case HotkeyAction.AltUp: ReleaseModifier(VkMenu); break;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }
    }

    /// <summary>Releases every modifier currently held. Must be called on disconnect/session-timeout/shutdown (spec #46, #153).</summary>
    public void ReleaseAllModifiers()
    {
        lock (_lock)
        {
            foreach (var vk in _heldModifiers.ToArray())
            {
                SendKey(vk, down: false);
                _heldModifiers.Remove(vk);
            }
        }
    }

    private void HoldModifier(ushort vk)
    {
        if (_heldModifiers.Add(vk))
        {
            SendKey(vk, down: true);
        }
    }

    private void ReleaseModifier(ushort vk)
    {
        if (_heldModifiers.Remove(vk))
        {
            SendKey(vk, down: false);
        }
    }

    private void PressCombo(ushort modifierVk, ushort keyVk)
    {
        var alreadyHeld = _heldModifiers.Contains(modifierVk);
        if (!alreadyHeld)
        {
            SendKey(modifierVk, down: true);
        }

        SendKey(keyVk, down: true);
        SendKey(keyVk, down: false);

        if (!alreadyHeld)
        {
            SendKey(modifierVk, down: false);
        }
    }

    private void TapKey(ushort vk)
    {
        SendKey(vk, down: true);
        SendKey(vk, down: false);
    }

    private void ClickRightMouseButton()
    {
        Send(new[]
        {
            new INPUT { type = (uint)INPUT_TYPE.INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = (uint)MOUSEEVENTF.RIGHTDOWN } } },
            new INPUT { type = (uint)INPUT_TYPE.INPUT_MOUSE, u = new INPUT_UNION { mi = new MOUSEINPUT { dwFlags = (uint)MOUSEEVENTF.RIGHTUP } } },
        });
    }

    private void SendKey(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = (uint)INPUT_TYPE.INPUT_KEYBOARD,
            u = new INPUT_UNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? (uint)KEYEVENTF.NONE : (uint)KEYEVENTF.KEYUP } },
        };
        Send(new[] { input });
    }

    private void Send(INPUT[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            _logger.LogError("SendInput sent {Sent}/{Total} events (Win32 error {Error}).", sent, inputs.Length, Marshal.GetLastWin32Error());
        }
    }

    private static HotkeyAction VkToModifierAction(ushort vk) => vk switch
    {
        VkControl => HotkeyAction.CtrlDown,
        VkShift => HotkeyAction.ShiftDown,
        VkMenu => HotkeyAction.AltDown,
        _ => throw new ArgumentOutOfRangeException(nameof(vk)),
    };

    public void Dispose() => ReleaseAllModifiers();
}

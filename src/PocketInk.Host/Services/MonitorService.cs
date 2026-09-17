using System.Windows.Forms;
using PocketInk.Core.Coordinates;
using PocketInk.Host.Interop;

namespace PocketInk.Host.Services;

/// <summary>
/// Enumerates Windows displays in virtual-screen coordinates, including
/// monitors with negative Left/Top (positioned above/left of primary)
/// (spec #37). Uses System.Windows.Forms.Screen rather than hand-rolled
/// EnumDisplayMonitors P/Invoke to minimize custom native surface area.
/// </summary>
public sealed class MonitorService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        return Screen.AllScreens
            .Select(screen => new MonitorInfo
            {
                DeviceId = screen.DeviceName,
                Name = $"{FriendlyDeviceName(screen.DeviceName)} — {screen.Bounds.Width}×{screen.Bounds.Height}",
                Left = screen.Bounds.X,
                Top = screen.Bounds.Y,
                Width = screen.Bounds.Width,
                Height = screen.Bounds.Height,
                IsPrimary = screen.Primary,
                DpiScale = TryGetDpiScale(screen),
            })
            .ToList();
    }

    public MonitorInfo? GetByDeviceId(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return null;
        }

        return GetMonitors().FirstOrDefault(m => m.DeviceId == deviceId);
    }

    public MonitorInfo GetPrimaryOrFirst()
    {
        var monitors = GetMonitors();
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    private static string FriendlyDeviceName(string deviceName) => deviceName.TrimStart('\\', '.');

    private static double TryGetDpiScale(Screen screen)
    {
        try
        {
            var center = new POINT
            {
                X = screen.Bounds.X + screen.Bounds.Width / 2,
                Y = screen.Bounds.Y + screen.Bounds.Height / 2,
            };

            var monitorHandle = NativeMethods.MonitorFromPoint(center, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (monitorHandle == IntPtr.Zero)
            {
                return 1.0;
            }

            var hr = NativeMethods.GetDpiForMonitor(monitorHandle, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _);
            return hr == 0 ? dpiX / 96.0 : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }
}

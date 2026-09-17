using PocketInk.Core.Models;
using PocketInk.Host.Services;

namespace PocketInk.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _tempFilePath = Path.Combine(Path.GetTempPath(), "PocketInkTestSettings_" + Guid.NewGuid() + ".json");

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new SettingsStore(_tempFilePath);

        var settings = store.Load();

        Assert.Equal(new AppSettings().WebServerPort, settings.WebServerPort);
        Assert.Equal(new AppSettings().Smoothing, settings.Smoothing);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new SettingsStore(_tempFilePath);
        var original = new AppSettings
        {
            SelectedMonitorDeviceId = @"\\.\DISPLAY2",
            PressureMode = PressureMode.VelocitySimulated,
            ConstantPressureFraction = 0.42,
            Smoothing = SmoothingLevel.Medium,
            WebServerPort = 55123,
            PreferredNetworkAdapterId = "adapter-1",
            VideoQuality = VideoQuality.High,
            VideoFps = 30,
            VideoResolution = "1920x1080",
            ToolbarVisible = false,
            TabletAspectMode = TabletAspectMode.Stretch,
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(original.SelectedMonitorDeviceId, loaded.SelectedMonitorDeviceId);
        Assert.Equal(original.PressureMode, loaded.PressureMode);
        Assert.Equal(original.ConstantPressureFraction, loaded.ConstantPressureFraction);
        Assert.Equal(original.Smoothing, loaded.Smoothing);
        Assert.Equal(original.WebServerPort, loaded.WebServerPort);
        Assert.Equal(original.PreferredNetworkAdapterId, loaded.PreferredNetworkAdapterId);
        Assert.Equal(original.VideoQuality, loaded.VideoQuality);
        Assert.Equal(original.VideoFps, loaded.VideoFps);
        Assert.Equal(original.VideoResolution, loaded.VideoResolution);
        Assert.Equal(original.ToolbarVisible, loaded.ToolbarVisible);
        Assert.Equal(original.TabletAspectMode, loaded.TabletAspectMode);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(_tempFilePath, "{ not valid json ");
        var store = new SettingsStore(_tempFilePath);

        var settings = store.Load();

        Assert.Equal(new AppSettings().WebServerPort, settings.WebServerPort);
    }
}

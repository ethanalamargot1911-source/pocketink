using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using PocketInk.Core.Models;
using PocketInk.Host.Networking;
using PocketInk.Host.Security;
using PocketInk.Host.Services;

namespace PocketInk.Host;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly SettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly WebHostService _webHostService = new();
    private readonly PairingService _pairingService = new(TimeProvider.System);
    private readonly HostServices _hostServices;
    private RemotePairingService _remotePairingService = null!;

    public static WebHostService WebHost { get; private set; } = null!;
    public static PairingService Pairing { get; private set; } = null!;
    public static RemotePairingService RemotePairing { get; private set; } = null!;
    public static HostServices Host { get; private set; } = null!;
    public static SettingsStore SettingsStore { get; private set; } = null!;

    public App()
    {
        _settings = _settingsStore.Load();
        _hostServices = new HostServices(_settings);
    }

    /// <summary>Persists whatever is currently on <see cref="HostServices.Settings"/> (spec #89).</summary>
    public static void PersistSettings() => SettingsStore.Save(Host.Settings);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _remotePairingService = new RemotePairingService(_hostServices, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        WebHost = _webHostService;
        Pairing = _pairingService;
        RemotePairing = _remotePairingService;
        Host = _hostServices;
        SettingsStore = _settingsStore;

        var webRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        await _webHostService.StartAsync(_settings.WebServerPort, webRootPath, _pairingService, _hostServices);

        var lanAddress = new LanAddressProvider().GetPreferredAddress(_settings.PreferredNetworkAdapterId) ?? "127.0.0.1";
        _pairingService.Initialize($"http://{lanAddress}:{_webHostService.Port}");
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _settingsStore.Save(_settings);
        await _webHostService.DisposeAsync();
        _hostServices.Dispose();
        base.OnExit(e);
    }
}

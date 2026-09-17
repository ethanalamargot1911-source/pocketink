using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PocketInk.Core.Coordinates;
using PocketInk.Core.Models;
using PocketInk.Host.Networking;

namespace PocketInk.Host;

/// <summary>
/// The host's control window: pairing QR, target monitor/pressure/smoothing
/// settings, live session metrics, and the safety controls (spec #89, #149).
/// Talks directly to the services on <see cref="App"/> since WPF and Kestrel
/// share one process - no IPC needed.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _isPopulatingSettingsUi;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateMonitorComboBox();
        PopulatePressureModeComboBox();
        PopulateSmoothingComboBox();
        PopulateNetworkAdapterComboBox();
        LoadQrImage();
        LoadRemoteSettingsUi();
        App.RemotePairing.PairingFailed += RemotePairing_PairingFailed;

        _refreshTimer.Tick += RefreshTimer_Tick;
        _refreshTimer.Start();
        RefreshTimer_Tick(this, EventArgs.Empty);
    }

    // ---- Periodic refresh ----

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        var connected = App.Host.ActiveSession is not null;
        ConnectionStatusText.Text = connected ? "iPhone connected" : "Waiting for iPhone…";

        PairedStatusText.Text = App.Pairing.IsPaired ? "Paired" : "Not paired yet";

        // Only one phone connects at a time (HostServices.ActiveSession), so if a Remote pairing
        // attempt is still showing its "waiting" QR once a session goes active, it's that attempt
        // succeeding - clear the stale QR rather than leave it up. (In the rare case a LAN device
        // connects while a Remote attempt is independently mid-handshake, this clears the QR a bit
        // early; harmless, since the QR itself remains scannable from RemotePairing's own state
        // until it actually completes or times out.)
        if (connected && CancelRemotePairingButton.IsEnabled)
        {
            RemoteQrBorder.Visibility = Visibility.Collapsed;
            RemoteStatusText.Text = "Connected.";
            StartRemotePairingButton.IsEnabled = true;
            CancelRemotePairingButton.IsEnabled = false;
        }

        var session = App.Host.ActiveSession;
        var metrics = session?.Metrics;
        MetricsText.Text =
            $"Contact active: {App.Host.PenService.IsContactActive}\n" +
            $"Packets received: {metrics?.Received ?? 0}\n" +
            $"Duplicates: {metrics?.Duplicates ?? 0}\n" +
            $"Out of order: {metrics?.OutOfOrderCount ?? 0}\n" +
            $"Estimated lost: {metrics?.EstimatedLost ?? 0}";

        if (App.Pairing.RefreshIfExpired())
        {
            LoadQrImage();
        }
    }

    // ---- Pairing ----

    private void LoadQrImage()
    {
        var pngBytes = App.Pairing.GeneratePairingQrPng();
        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(pngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();
        QrImage.Source = bitmap;
    }

    private void ForgetDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "This disconnects any paired iPhone and requires rescanning a new QR code. Continue?",
            "Forget paired iPhone", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        App.Pairing.ForgetPairedDevice();
        LoadQrImage();
    }

    private void RefreshQrButton_Click(object sender, RoutedEventArgs e)
    {
        App.Pairing.RefreshPairingToken();
        LoadQrImage();
    }

    // ---- Remote pairing (spec Phase 5: cross-network via Supabase) ----

    private void LoadRemoteSettingsUi()
    {
        _isPopulatingSettingsUi = true;
        SupabaseUrlTextBox.Text = App.Host.Settings.SupabaseUrl ?? "";
        SupabaseAnonKeyTextBox.Text = App.Host.Settings.SupabaseAnonKey ?? "";
        RemoteClientBaseUrlTextBox.Text = App.Host.Settings.RemoteClientBaseUrl ?? "";
        _isPopulatingSettingsUi = false;
    }

    private void RemoteSettingsTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingSettingsUi)
        {
            return;
        }

        App.Host.Settings.SupabaseUrl = NullIfEmpty(SupabaseUrlTextBox.Text);
        App.Host.Settings.SupabaseAnonKey = NullIfEmpty(SupabaseAnonKeyTextBox.Text);
        App.Host.Settings.RemoteClientBaseUrl = NullIfEmpty(RemoteClientBaseUrlTextBox.Text);
        App.PersistSettings();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void StartRemotePairingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!App.RemotePairing.IsConfigured)
        {
            System.Windows.MessageBox.Show(
                "Fill in the Supabase project URL, anon key, and remote client URL above first.",
                "Remote setup incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string pairingCode;
        try
        {
            pairingCode = App.RemotePairing.StartPairing(CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Remote pairing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var pngBytes = App.RemotePairing.GeneratePairingQrPng(pairingCode);
        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(pngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();
        RemoteQrImage.Source = bitmap;
        RemoteQrBorder.Visibility = Visibility.Visible;
        RemoteStatusText.Text = "Waiting for the phone to scan and connect… (times out after 30 seconds)";
        StartRemotePairingButton.IsEnabled = false;
        CancelRemotePairingButton.IsEnabled = true;
    }

    private async void CancelRemotePairingButton_Click(object sender, RoutedEventArgs e)
    {
        await App.RemotePairing.CancelPairingAsync();
        RemoteQrBorder.Visibility = Visibility.Collapsed;
        RemoteStatusText.Text = "";
        StartRemotePairingButton.IsEnabled = true;
        CancelRemotePairingButton.IsEnabled = false;
    }

    private void RemotePairing_PairingFailed(string reason)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RemoteQrBorder.Visibility = Visibility.Collapsed;
            RemoteStatusText.Text = "Remote pairing failed: " + reason;
            StartRemotePairingButton.IsEnabled = true;
            CancelRemotePairingButton.IsEnabled = false;
        });
    }

    // ---- Settings ----

    private void PopulateMonitorComboBox()
    {
        var monitors = App.Host.Monitors.GetMonitors();
        MonitorComboBox.ItemsSource = monitors;

        _isPopulatingSettingsUi = true;
        MonitorComboBox.SelectedItem = monitors.FirstOrDefault(m => m.DeviceId == App.Host.Settings.SelectedMonitorDeviceId)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors.FirstOrDefault();
        _isPopulatingSettingsUi = false;
    }

    private void PopulatePressureModeComboBox()
    {
        _isPopulatingSettingsUi = true;
        PressureModeComboBox.ItemsSource = Enum.GetValues<PressureMode>();
        PressureModeComboBox.SelectedItem = App.Host.Settings.PressureMode;
        ConstantPressureSlider.Value = App.Host.Settings.ConstantPressureFraction;
        UpdateConstantPressureLabel();
        UpdateConstantPressureSliderEnabled();
        _isPopulatingSettingsUi = false;
    }

    private void PopulateSmoothingComboBox()
    {
        _isPopulatingSettingsUi = true;
        SmoothingComboBox.ItemsSource = Enum.GetValues<SmoothingLevel>();
        SmoothingComboBox.SelectedItem = App.Host.Settings.Smoothing;
        _isPopulatingSettingsUi = false;
    }

    private void MonitorComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettingsUi || MonitorComboBox.SelectedItem is not MonitorInfo monitor)
        {
            return;
        }

        App.Host.Settings.SelectedMonitorDeviceId = monitor.DeviceId;
        App.PersistSettings();
    }

    private void PressureModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettingsUi || PressureModeComboBox.SelectedItem is not PressureMode mode)
        {
            return;
        }

        App.Host.Settings.PressureMode = mode;
        UpdateConstantPressureSliderEnabled();
        App.PersistSettings();
    }

    private void ConstantPressureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isPopulatingSettingsUi)
        {
            return;
        }

        App.Host.Settings.ConstantPressureFraction = ConstantPressureSlider.Value;
        UpdateConstantPressureLabel();
        App.PersistSettings();
    }

    private void SmoothingComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettingsUi || SmoothingComboBox.SelectedItem is not SmoothingLevel level)
        {
            return;
        }

        App.Host.Settings.Smoothing = level;
        App.PersistSettings();
    }

    /// <summary>One entry in the network-adapter picker. AdapterId is null for "Automatic".</summary>
    private sealed record NetworkAdapterOption(string? AdapterId, string Label);

    private void PopulateNetworkAdapterComboBox()
    {
        // Only list adapters that could actually be selected (matches LanAddressSelector's own
        // candidate filter) - showing a down or address-less adapter here would just be confusing,
        // since picking it wouldn't change anything.
        var adapters = new LanAddressProvider().GetAdapters()
            .Where(a => a.IsUp && !a.IsLoopback && !string.IsNullOrEmpty(a.IPv4Address))
            .ToList();

        var options = new List<NetworkAdapterOption> { new(null, "(Automatic)") };
        options.AddRange(adapters.Select(a => new NetworkAdapterOption(a.Id, $"{a.DisplayName} — {a.IPv4Address}")));

        _isPopulatingSettingsUi = true;
        NetworkAdapterComboBox.ItemsSource = options;
        NetworkAdapterComboBox.SelectedItem = options.FirstOrDefault(o => o.AdapterId == App.Host.Settings.PreferredNetworkAdapterId)
            ?? options[0];
        _isPopulatingSettingsUi = false;
    }

    private void NetworkAdapterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isPopulatingSettingsUi || NetworkAdapterComboBox.SelectedItem is not NetworkAdapterOption option)
        {
            return;
        }

        App.Host.Settings.PreferredNetworkAdapterId = option.AdapterId;
        App.PersistSettings();

        // Take effect immediately, not just on next restart - re-resolve the address now and
        // regenerate the QR so a phone scanning right after this change gets the right one.
        var lanAddress = new LanAddressProvider().GetPreferredAddress(option.AdapterId) ?? "127.0.0.1";
        App.Pairing.Initialize($"http://{lanAddress}:{App.WebHost.Port}");
        LoadQrImage();
    }

    private void UpdateConstantPressureLabel()
    {
        ConstantPressureLabel.Text = $"Constant pressure: {ConstantPressureSlider.Value:P0}";
    }

    private void UpdateConstantPressureSliderEnabled()
    {
        ConstantPressureSlider.IsEnabled = App.Host.Settings.PressureMode == PressureMode.Constant;
    }

    // ---- Diagnostics / safety ----

    private void TestPenButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "This injects a real synthetic pen stroke on the target monitor, wherever the cursor lands. " +
            "Focus a drawing app there first. Continue?",
            "Test Pen", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        var monitor = App.Host.Monitors.GetByDeviceId(App.Host.Settings.SelectedMonitorDeviceId)
            ?? App.Host.Monitors.GetPrimaryOrFirst();

        var (startX, startY) = CoordinateMapper.NormalizedToScreen(monitor, 0.45, 0.45);
        var (endX, endY) = CoordinateMapper.NormalizedToScreen(monitor, 0.55, 0.55);

        App.Host.PenService.PenDown(startX, startY, PressureConverter.DefaultPressure);
        App.Host.PenService.PenMove(endX, endY, PressureConverter.DefaultPressure);
        App.Host.PenService.PenUp(endX, endY);
    }

    private void StopInputButton_Click(object sender, RoutedEventArgs e)
    {
        App.Host.PenService.Cancel();
        App.Host.Hotkeys.ReleaseAllModifiers();
    }
}

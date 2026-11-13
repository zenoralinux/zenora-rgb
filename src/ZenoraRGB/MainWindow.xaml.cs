using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ZenoraRGB.Models;
using ZenoraRGB.Services;

namespace ZenoraRGB;

public partial class MainWindow : Window
{
    private readonly OpenRgbService _engine = new();
    private readonly ObservableCollection<OpenRgbDevice> _devices = new();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        DeviceCombo.ItemsSource = _devices;
        HexColorText.Text = "#8B75FF";
        DeviceCombo.IsEnabled = false;
        Log("Zenora RGB 0.3.0 started.");
        Log("The app uses OpenRGB for vendor-independent device control.");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // First-run setup and hardware detection happen automatically.
        await ConnectAndScanAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e) =>
        await ConnectAndScanAsync();

    private async Task ConnectAndScanAsync()
    {
        if (_busy) return;

        SetBusy(true);
        StatusText.Text = "Preparing engine...";
        EngineText.Text = "Preparing the embedded drivers and native engine. No runtime downloads are required.";

        try
        {
            var devices = await _engine.ConnectAsync(Log);
            SetDevices(devices);
            StatusText.Text = "Connected";
            EngineText.Text = $"OpenRGB {OpenRgbRuntimeInstaller.OpenRgbVersion} is running. Hardware scan completed.";
            DriverText.Text = "Embedded prerequisites checked. Read the activity log for any driver warning or restart requirement.";
            Log($"Scan completed. OpenRGB reported {devices.Count} controller(s).");

            if (devices.Count == 0)
            {
                SelectedDeviceText.Text = "OpenRGB is running, but no compatible RGB controllers were detected.";
                MessageBox.Show(
                    "OpenRGB started successfully but did not detect any compatible RGB devices. Close other RGB-control utilities, check device compatibility, then scan again.",
                    "No compatible devices found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Connection failed";
            EngineText.Text = "The independent RGB engine could not be started.";
            DriverText.Text = ex.Message;
            Log($"Connection failed: {ex.Message}");
            MessageBox.Show(
                ex.Message + Environment.NewLine + Environment.NewLine +
                "Verify that you are using the complete v0.3.0 EXE. Check Windows permissions and free disk space. If PawnIO was installed, restart Windows and try again.",
                "Zenora RGB", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_engine.IsConnected)
        {
            await ConnectAndScanAsync();
            return;
        }

        SetBusy(true);
        try
        {
            var devices = await _engine.RefreshAsync();
            SetDevices(devices);
            StatusText.Text = "Connected";
            Log($"Hardware refreshed. Found {devices.Count} controller(s).");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Refresh failed";
            Log($"Refresh failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Hardware scan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private void SetDevices(IReadOnlyList<OpenRgbDevice> devices)
    {
        var previousId = (DeviceCombo.SelectedItem as OpenRgbDevice)?.Id;
        _devices.Clear();
        foreach (var device in devices) _devices.Add(device);

        var selectedIndex = previousId.HasValue
            ? _devices.ToList().FindIndex(d => d.Id == previousId.Value)
            : -1;
        DeviceCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (_devices.Count > 0 ? 0 : -1);
        DeviceCombo.IsEnabled = _devices.Count > 0;
        DeviceCountText.Text = _devices.Count == 1
            ? "1 RGB controller detected"
            : $"{_devices.Count} RGB controllers detected";
        UpdateSelectedDeviceDescription();
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateSelectedDeviceDescription();

    private void UpdateSelectedDeviceDescription()
    {
        if (SelectedDeviceText is null) return;
        if (DeviceCombo.SelectedItem is not OpenRgbDevice device)
        {
            SelectedDeviceText.Text = "No controller selected.";
            return;
        }

        SelectedDeviceText.Text =
            $"{device.DisplayName}{Environment.NewLine}" +
            $"LED count: {device.LedCount}{Environment.NewLine}" +
            $"Zones: {device.ZoneCount}{Environment.NewLine}" +
            $"OpenRGB controller ID: {device.Id}";
    }

    private async void ApplySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceCombo.SelectedItem is not OpenRgbDevice device)
        {
            MessageBox.Show("Connect to the engine and select a device first.",
                "No device selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetRgb(out var red, out var green, out var blue)) return;

        SetBusy(true);
        try
        {
            await _engine.ApplyToDeviceAsync(device, red, green, blue);
            Log($"Applied #{red:X2}{green:X2}{blue:X2} to {device.DisplayName}.");
        }
        catch (Exception ex)
        {
            Log($"Color update failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Color update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private async void ApplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_devices.Count == 0)
        {
            MessageBox.Show("Connect to the engine and scan for devices first.",
                "No devices found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryGetRgb(out var red, out var green, out var blue)) return;

        SetBusy(true);
        try
        {
            var count = await _engine.ApplyToAllAsync(_devices, red, green, blue);
            Log($"Applied #{red:X2}{green:X2}{blue:X2} to {count} controller(s).");
        }
        catch (Exception ex)
        {
            Log($"Apply-all failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Apply color", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string color)
            HexColorText.Text = color;
    }

    private void HexColorText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ColorPreview is null || HexColorText is null) return;
        if (TryParseColor(HexColorText.Text, out var color))
            ColorPreview.Background = new SolidColorBrush(color);
    }

    private bool TryGetRgb(out byte red, out byte green, out byte blue)
    {
        if (!TryParseColor(HexColorText.Text, out var color))
        {
            MessageBox.Show("Enter a valid HEX color, for example #8B75FF.",
                "Invalid color", MessageBoxButton.OK, MessageBoxImage.Warning);
            red = green = blue = 0;
            return false;
        }
        red = color.R;
        green = color.G;
        blue = color.B;
        return true;
    }

    private static bool TryParseColor(string? value, out Color color)
    {
        try
        {
            var result = ColorConverter.ConvertFromString(value?.Trim());
            if (result is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch { /* invalid HEX text */ }
        color = Colors.White;
        return false;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ConnectButton.IsEnabled = !busy;
        ConnectButton.Content = busy
            ? "Please wait..."
            : (_engine.IsConnected ? "Reconnect and scan" : "Connect and scan");
        DeviceCombo.IsEnabled = !busy && _devices.Count > 0;
    }

    private void Log(string message)
    {
        if (LogText is null) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText.Text = string.IsNullOrWhiteSpace(LogText.Text)
            ? line
            : LogText.Text + Environment.NewLine + line;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        _engine.Dispose();
}
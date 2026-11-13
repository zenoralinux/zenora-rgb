using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ZenoraRGB.Models;
using ZenoraRGB.Services;

namespace ZenoraRGB;

public partial class MainWindow : Window
{
    private readonly AsusAuraService _asus = new();
    private readonly MsiMysticLightService _msi = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<RgbDevice> _msiDevices = new();

    public MainWindow()
    {
        InitializeComponent();
        MsiZoneCombo.ItemsSource = _msiDevices;
        MsiSdkPathText.Text = string.IsNullOrWhiteSpace(_settings.MsiSdkPath)
            ? "Not configured"
            : _settings.MsiSdkPath;
        HexColorText.Text = "#8B75FF";
        Log("Zenora RGB started. Hardware access stays on this PC.");
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        Log("Checking vendor SDK availability...");
        var auraType = Type.GetTypeFromProgID("aura.sdk.1", throwOnError: false);
        AsusStatusText.Text = auraType is null ? "Aura SDK not detected" : "Aura SDK registered";
        AsusDeviceText.Text = auraType is null
            ? "Install or repair ASUS Armoury Crate / Lighting Service."
            : "Aura SDK COM component is registered. Connect to enumerate devices.";

        MsiStatusText.Text = !string.IsNullOrWhiteSpace(_settings.MsiSdkPath) && File.Exists(_settings.MsiSdkPath)
            ? "MSI SDK DLL configured"
            : "SDK not selected";
        MsiDeviceText.Text = "MSI Center / Mystic Light must also be installed.";
        Log("Scan complete. Vendor SDK status refreshed.");
    }

    private void ConnectAsusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var devices = _asus.Connect();
            AsusStatusText.Text = $"Connected · {devices.Count} device(s)";
            AsusDeviceText.Text = string.Join(" · ", devices.Select(d => d.Details));
            Log($"ASUS Aura connected. Found {devices.Count} device(s).");
        }
        catch (Exception ex)
        {
            AsusStatusText.Text = "Connection unavailable";
            AsusDeviceText.Text = ex.Message;
            Log($"ASUS: {ex.Message}");
        }
    }

    private void ReleaseAsusButton_Click(object sender, RoutedEventArgs e)
    {
        _asus.Release();
        AsusStatusText.Text = "Released";
        AsusDeviceText.Text = "Control returned to ASUS lighting software.";
        Log("ASUS Aura control released.");
    }

    private void SelectMsiSdkButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the official MSI Mystic Light SDK x64 DLL",
            Filter = "MSI Mystic Light SDK (MysticLight_SDK_x64.dll)|MysticLight_SDK_x64.dll|DLL files (*.dll)|*.dll",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _settings.MsiSdkPath = dialog.FileName;
        _settings.Save();
        MsiSdkPathText.Text = dialog.FileName;
        MsiStatusText.Text = "SDK DLL selected";
        MsiDeviceText.Text = "Click Connect MSI to initialize the vendor SDK.";
        Log("Saved MSI SDK DLL path.");
    }

    private void ConnectMsiButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.MsiSdkPath))
                throw new InvalidOperationException("Select MysticLight_SDK_x64.dll first. The official SDK is not bundled with this app.");

            var devices = _msi.Connect(_settings.MsiSdkPath);
            _msiDevices.Clear();
            foreach (var device in devices)
                _msiDevices.Add(device);

            if (_msiDevices.Count > 0)
                MsiZoneCombo.SelectedIndex = 0;

            MsiStatusText.Text = $"Connected · {_msiDevices.Count} LED zone(s)";
            MsiDeviceText.Text = "Select a zone or apply the color to all detected MSI zones.";
            Log($"MSI Mystic Light connected. Found {_msiDevices.Count} LED zone(s).");
        }
        catch (Exception ex)
        {
            MsiStatusText.Text = "Connection unavailable";
            MsiDeviceText.Text = ex.Message;
            Log($"MSI: {ex.Message}");
        }
    }

    private void ReleaseMsiButton_Click(object sender, RoutedEventArgs e)
    {
        _msi.Release();
        _msiDevices.Clear();
        MsiStatusText.Text = "Released";
        MsiDeviceText.Text = "MSI SDK released.";
        Log("MSI Mystic Light SDK released.");
    }

    private void ApplyAsusButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (red, green, blue) = GetSelectedRgb();
            var count = _asus.ApplyColor(red, green, blue);
            Log($"Applied #{red:X2}{green:X2}{blue:X2} to {count} ASUS Aura device(s).");
        }
        catch (Exception ex)
        {
            Log($"ASUS apply failed: {ex.Message}");
            MessageBox.Show(ex.Message, "ASUS Aura", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyMsiButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (MsiZoneCombo.SelectedItem is not RgbDevice selected)
                throw new InvalidOperationException("Connect MSI and select an LED zone first.");

            var (red, green, blue) = GetSelectedRgb();
            var count = _msi.ApplyColor(red, green, blue, selected);
            Log($"Applied color to {count} MSI zone(s): {selected.DisplayName}.");
        }
        catch (Exception ex)
        {
            Log($"MSI apply failed: {ex.Message}");
            MessageBox.Show(ex.Message, "MSI Mystic Light", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyBothButton_Click(object sender, RoutedEventArgs e)
    {
        var (red, green, blue) = GetSelectedRgb();
        var success = new List<string>();
        var errors = new List<string>();

        try
        {
            if (_asus.IsConnected)
                success.Add($"ASUS: {_asus.ApplyColor(red, green, blue)} device(s)");
            else
                errors.Add("ASUS is not connected");
        }
        catch (Exception ex) { errors.Add($"ASUS: {ex.Message}"); }

        try
        {
            if (_msi.IsConnected)
                success.Add($"MSI: {_msi.ApplyColor(red, green, blue)} zone(s)");
            else
                errors.Add("MSI is not connected");
        }
        catch (Exception ex) { errors.Add($"MSI: {ex.Message}"); }

        if (success.Count > 0)
            Log($"Color applied ({string.Join(" · ", success)}).");

        if (errors.Count > 0)
        {
            Log(string.Join(" | ", errors));
            MessageBox.Show(string.Join(Environment.NewLine, errors), "Some devices were skipped",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void PresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string color)
            HexColorText.Text = color;
    }

    private void HexColorText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ColorPreview is null || HexColorText is null)
            return;

        if (TryGetColor(out var color))
            ColorPreview.Background = new SolidColorBrush(color);
    }

    private (byte Red, byte Green, byte Blue) GetSelectedRgb()
    {
        if (!TryGetColor(out var color))
            throw new InvalidOperationException("Enter a valid HEX color such as #8B75FF.");

        return (color.R, color.G, color.B);
    }

    private bool TryGetColor(out Color color)
    {
        try
        {
            var converted = new ColorConverter().ConvertFromString(HexColorText.Text.Trim());
            if (converted is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch { /* invalid input is handled by the caller */ }

        color = Colors.White;
        return false;
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText.Text = string.IsNullOrWhiteSpace(LogText.Text) ? line : LogText.Text + Environment.NewLine + line;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _asus.Dispose();
        _msi.Dispose();
    }
}

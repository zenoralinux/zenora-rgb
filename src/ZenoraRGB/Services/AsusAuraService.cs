using System.Collections.Generic;
using System.Runtime.InteropServices;
using ZenoraRGB.Models;

namespace ZenoraRGB.Services;

/// <summary>
/// Uses the ASUS Aura SDK v3.1 COM server installed by ASUS Lighting Service.
/// The application does not bundle ASUS components.
/// </summary>
public sealed class AsusAuraService : IDisposable
{
    private object? _sdk;
    private bool _disposed;

    public bool IsConnected => _sdk is not null;

    public IReadOnlyList<RgbDevice> Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sdk is not null)
            return EnumerateDevices();

        var sdkType = Type.GetTypeFromProgID("aura.sdk.1", throwOnError: false);
        if (sdkType is null)
        {
            throw new InvalidOperationException(
                "ASUS Aura SDK was not found. Install or repair Armoury Crate / ASUS Lighting Service, then restart Windows.");
        }

        _sdk = Activator.CreateInstance(sdkType)
            ?? throw new InvalidOperationException("Could not create the ASUS Aura SDK COM object.");

        try
        {
            dynamic sdk = _sdk;
            sdk.SwitchMode();
            var devices = EnumerateDevices();

            if (devices.Count == 0)
            {
                throw new InvalidOperationException(
                    "Aura SDK is installed, but it did not report any controllable Aura devices. Check Armoury Crate and the device's Aura compatibility.");
            }

            return devices;
        }
        catch
        {
            ReleaseSdk();
            throw;
        }
    }

    public IReadOnlyList<RgbDevice> EnumerateDevices()
    {
        if (_sdk is null)
            throw new InvalidOperationException("Connect to ASUS Aura before scanning devices.");

        dynamic sdk = _sdk;
        dynamic devices = sdk.Enumerate(0);
        var result = new List<RgbDevice>();
        var deviceNumber = 0;

        foreach (dynamic device in devices)
        {
            deviceNumber++;
            var ledCount = 0;
            foreach (dynamic light in device.Lights)
                ledCount++;

            result.Add(new RgbDevice(
                "ASUS Aura",
                $"Aura device {deviceNumber}",
                $"AURA-{deviceNumber}",
                0,
                $"{ledCount} LED(s)"));
        }

        return result;
    }

    public int ApplyColor(byte red, byte green, byte blue)
    {
        if (_sdk is null)
            throw new InvalidOperationException("Connect to ASUS Aura before applying a color.");

        // Aura SDK documents IAuraRgbLight.Color as 0x00GGBBRR.
        var auraColor = (uint)((blue << 16) | (green << 8) | red);
        dynamic sdk = _sdk;
        dynamic devices = sdk.Enumerate(0);
        var applied = 0;

        foreach (dynamic device in devices)
        {
            foreach (dynamic light in device.Lights)
                light.Color = auraColor;

            device.Apply();
            applied++;
        }

        return applied;
    }

    public void Release()
    {
        if (_sdk is null)
            return;

        try
        {
            dynamic sdk = _sdk;
            sdk.ReleaseControl();
        }
        catch
        {
            // Older SDK registrations may not expose the explicit release method.
        }
        finally
        {
            ReleaseSdk();
        }
    }

    private void ReleaseSdk()
    {
        if (_sdk is not null && Marshal.IsComObject(_sdk))
        {
            try { Marshal.FinalReleaseComObject(_sdk); }
            catch { /* best-effort COM cleanup */ }
        }

        _sdk = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Release();
        _disposed = true;
    }
}

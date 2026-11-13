using System.Runtime.InteropServices;
using ZenoraRGB.Models;

namespace ZenoraRGB.Services;

/// <summary>
/// Late-binds the official MSI Mystic Light SDK DLL.
/// MSI SDK binaries are proprietary and are intentionally not bundled.
/// </summary>
public sealed class MsiMysticLightService : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReleaseDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDeviceInfoDelegate(
        [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] deviceTypes,
        [Out, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_BSTR)] out string[] ledCounts);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int SetLedColorDelegate(
        [MarshalAs(UnmanagedType.BStr)] string deviceType,
        uint zoneIndex,
        uint red,
        uint green,
        uint blue);

    private IntPtr _library;
    private InitializeDelegate? _initialize;
    private ReleaseDelegate? _release;
    private GetDeviceInfoDelegate? _getDeviceInfo;
    private SetLedColorDelegate? _setLedColor;
    private bool _initialized;
    private bool _disposed;

    public string? LoadedDllPath { get; private set; }
    public IReadOnlyList<RgbDevice> Devices { get; private set; } = Array.Empty<RgbDevice>();
    public bool IsConnected => _initialized;

    public IReadOnlyList<RgbDevice> Connect(string dllPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
            throw new FileNotFoundException("Select the 64-bit MysticLight_SDK_x64.dll from the official MSI SDK package.");

        Release();

        try
        {
            _library = NativeLibrary.Load(Path.GetFullPath(dllPath));
            _initialize = Load<InitializeDelegate>("MLAPI_Initialize");
            _release = Load<ReleaseDelegate>("MLAPI_Release");
            _getDeviceInfo = Load<GetDeviceInfoDelegate>("MLAPI_GetDeviceInfo");
            _setLedColor = Load<SetLedColorDelegate>("MLAPI_SetLedColor");

            var initStatus = _initialize();
            if (initStatus != 0)
            {
                throw new InvalidOperationException(
                    $"MSI Mystic Light SDK initialization failed (code {initStatus}). Make sure MSI Center / Mystic Light is installed and supported.");
            }

            _initialized = true;
            LoadedDllPath = Path.GetFullPath(dllPath);

            var status = _getDeviceInfo(out var deviceTypes, out var ledCounts);
            if (status != 0)
                throw new InvalidOperationException($"MSI could not enumerate devices (SDK code {status}).");

            var result = new List<RgbDevice>();
            var length = Math.Min(deviceTypes.Length, ledCounts.Length);

            for (var i = 0; i < length; i++)
            {
                if (!uint.TryParse(ledCounts[i], out var zoneCount))
                    continue;

                zoneCount = Math.Min(zoneCount, 256);
                for (uint zone = 0; zone < zoneCount; zone++)
                {
                    result.Add(new RgbDevice(
                        "MSI Mystic Light",
                        $"{deviceTypes[i]} · Zone {zone + 1}",
                        deviceTypes[i],
                        zone,
                        $"LED area {zone}"));
                }
            }

            Devices = result;
            if (Devices.Count == 0)
                throw new InvalidOperationException("MSI SDK loaded, but no controllable LED zones were returned.");

            return Devices;
        }
        catch
        {
            Release();
            throw;
        }
    }

    public int ApplyColor(byte red, byte green, byte blue, RgbDevice? selectedDevice = null)
    {
        if (!_initialized || _setLedColor is null)
            throw new InvalidOperationException("Connect to MSI Mystic Light before applying a color.");

        IEnumerable<RgbDevice> targets = selectedDevice is null
            ? Devices
            : new[] { selectedDevice };

        var applied = 0;
        foreach (var device in targets)
        {
            var status = _setLedColor(
                device.DeviceType,
                device.ZoneIndex,
                red,
                green,
                blue);

            if (status != 0)
                throw new InvalidOperationException(
                    $"MSI failed to set {device.DisplayName} (SDK code {status}).");

            applied++;
        }

        return applied;
    }

    private T Load<T>(string exportName) where T : Delegate
    {
        if (_library == IntPtr.Zero)
            throw new InvalidOperationException("MSI SDK library is not loaded.");

        if (!NativeLibrary.TryGetExport(_library, exportName, out var address))
            throw new EntryPointNotFoundException($"The selected MSI SDK DLL does not export {exportName}.");

        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    public void Release()
    {
        if (_initialized)
        {
            try { _release?.Invoke(); }
            catch { /* best-effort native SDK cleanup */ }
        }

        _initialized = false;
        _initialize = null;
        _release = null;
        _getDeviceInfo = null;
        _setLedColor = null;
        Devices = Array.Empty<RgbDevice>();
        LoadedDllPath = null;

        if (_library != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_library); }
            catch { /* best-effort native library cleanup */ }
            _library = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Release();
        _disposed = true;
    }
}

using System.Diagnostics;
using System.Net.Sockets;
using ZenoraRGB.Models;

namespace ZenoraRGB.Services;

public sealed class OpenRgbService : IDisposable
{
    private readonly OpenRgbSdkClient _client = new();
    private Process? _serverProcess;
    private bool _ownsServer;
    private bool _disposed;

    public bool IsConnected => _client.IsConnected;

    public async Task<IReadOnlyList<OpenRgbDevice>> ConnectAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        report?.Invoke("Preparing the OpenRGB runtime...");
        var executable = await OpenRgbRuntimeInstaller.EnsureOpenRgbAsync(report, cancellationToken);

        report?.Invoke("Checking the independent hardware-access driver...");
        try
        {
            var installCode = await OpenRgbRuntimeInstaller.InstallPawnIoAsync(report, cancellationToken);
            if (installCode == 3010)
                report?.Invoke("A restart may be required for motherboard and RGB memory access.");
        }
        catch (Exception ex)
        {
            // OpenRGB can still control devices that do not need SMBus/PawnIO.
            report?.Invoke($"PawnIO is unavailable: {ex.Message} Direct USB/PCIe devices will still be scanned.");
        }

        if (!await TryConnectExistingServerAsync(cancellationToken))
        {
            StartServer(executable);
            await ConnectToStartedServerAsync(report, cancellationToken);
        }

        report?.Invoke("Scanning RGB controllers...");
        var devices = await _client.GetDevicesAsync(cancellationToken);
        return devices;
    }

    public async Task<IReadOnlyList<OpenRgbDevice>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connect to the OpenRGB engine first.");

        return await _client.GetDevicesAsync(cancellationToken);
    }

    public async Task<int> ApplyToDeviceAsync(
        OpenRgbDevice device,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connect to the OpenRGB engine first.");

        await _client.ApplyColorAsync(device, red, green, blue, cancellationToken);
        return 1;
    }

    public async Task<int> ApplyToAllAsync(
        IReadOnlyList<OpenRgbDevice> devices,
        byte red,
        byte green,
        byte blue,
        CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Connect to the OpenRGB engine first.");

        var applied = 0;
        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (device.LedCount <= 0)
                continue;

            await _client.ApplyColorAsync(device, red, green, blue, cancellationToken);
            applied++;
            await Task.Delay(150, cancellationToken);
        }

        return applied;
    }

    private async Task<bool> TryConnectExistingServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await _client.ConnectAsync(timeout.Token);
            return true;
        }
        catch
        {
            _client.Dispose();
            return false;
        }
    }

    private void StartServer(string executable)
    {
        if (_serverProcess is { HasExited: false })
            return;

        var start = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--server --server-host 127.0.0.1 --server-port 6742",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _serverProcess = Process.Start(start)
            ?? throw new InvalidOperationException("Windows could not start the OpenRGB hardware engine.");

        _ownsServer = true;
    }

    private async Task ConnectToStartedServerAsync(
        Action<string>? report,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 50; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_serverProcess is { HasExited: true })
                throw new InvalidOperationException(
                    $"OpenRGB exited during startup (code {_serverProcess.ExitCode}).");

            try
            {
                await _client.ConnectAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException)
            {
                lastError = ex;
                _client.Dispose();
                await Task.Delay(400, cancellationToken);
            }
        }

        throw new InvalidOperationException(
            "OpenRGB did not start its local control server. " + (lastError?.Message ?? string.Empty));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.Dispose();

        if (_ownsServer && _serverProcess is not null)
        {
            try
            {
                if (!_serverProcess.HasExited)
                    _serverProcess.Kill(entireProcessTree: true);
            }
            catch { /* best-effort cleanup */ }

            _serverProcess.Dispose();
            _serverProcess = null;
        }
    }
}
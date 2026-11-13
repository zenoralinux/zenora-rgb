using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace ZenoraRGB.Services;

public static class OpenRgbRuntimeInstaller
{
    public const string OpenRgbVersion = "1.1";
    private const string OpenRgbArchiveUrl =
        "https://github.com/zenoralinux/zenora-rgb/releases/download/runtime-v1.1/OpenRGB_1.1_Windows_64_8e16de2.zip";
    private const string OpenRgbSha256 =
        "63291726C6FC84EB479EF2EE752E97E1187F6CE9C49FD27EEE40DED588A8172A";

    private const string PawnIoVersion = "2.2.0";
    private const string PawnIoUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    private const string PawnIoSha256 =
        "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const long MaxDownloadBytes = 180L * 1024 * 1024;
    private const long MaxExpandedBytes = 420L * 1024 * 1024;

    private static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenoraRGB", "Runtime");

    private static string OpenRgbDirectory =>
        Path.Combine(RootDirectory, $"OpenRGB-{OpenRgbVersion}");

    private static string PawnIoDirectory =>
        Path.Combine(RootDirectory, $"PawnIO-{PawnIoVersion}");

    public static async Task<string> EnsureOpenRgbAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);
        var executable = Directory.GetFiles(OpenRgbDirectory, "OpenRGB.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (executable is not null)
            return executable;

        report?.Invoke("Downloading the OpenRGB runtime from the Zenora GitHub release...");
        var archivePath = Path.Combine(RootDirectory, $"OpenRGB-{OpenRgbVersion}.zip");

        await DownloadVerifiedAsync(
            OpenRgbArchiveUrl, archivePath, OpenRgbSha256, cancellationToken);

        try
        {
            SafeExtractZip(archivePath, OpenRgbDirectory);
        }
        finally
        {
            TryDelete(archivePath);
        }

        executable = Directory.GetFiles(OpenRgbDirectory, "OpenRGB.exe", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (executable is null)
            throw new FileNotFoundException(
                "The OpenRGB runtime archive was downloaded, but OpenRGB.exe was not found.");

        report?.Invoke($"OpenRGB {OpenRgbVersion} is ready.");
        return executable;
    }

    public static async Task<string> EnsurePawnIoInstallerAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(PawnIoDirectory);
        var installerPath = Path.Combine(PawnIoDirectory, "PawnIO_setup.exe");

        if (!File.Exists(installerPath) ||
            !string.Equals(await ComputeSha256Async(installerPath, cancellationToken),
                PawnIoSha256, StringComparison.OrdinalIgnoreCase))
        {
            report?.Invoke("Downloading the signed PawnIO hardware-access driver...");
            await DownloadVerifiedAsync(PawnIoUrl, installerPath, PawnIoSha256, cancellationToken);
        }

        return installerPath;
    }

    public static async Task<int> InstallPawnIoAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        var driverPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "PawnIO.sys");

        if (File.Exists(driverPath))
        {
            report?.Invoke("PawnIO driver is already present.");
            return 0;
        }

        var installer = await EnsurePawnIoInstallerAsync(report, cancellationToken);
        report?.Invoke("Installing PawnIO silently. Windows administrator approval is required once.");

        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installer,
            Arguments = "-install -silent",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(installer)!
        };

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("Windows could not start the PawnIO installer.");

        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch { /* best-effort cancellation */ }
        });

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode is not 0 and not 3010)
            throw new InvalidOperationException(
                $"PawnIO installation failed with exit code {process.ExitCode}.");

        if (!File.Exists(driverPath))
        {
            if (process.ExitCode == 3010)
                report?.Invoke("PawnIO requires a Windows restart before SMBus/RAM access is available.");
            else
                report?.Invoke("PawnIO installer completed; Windows may need a restart before the driver becomes active.");
        }
        else
        {
            report?.Invoke("PawnIO driver installation completed.");
        }

        return process.ExitCode;
    }

    private static async Task DownloadVerifiedAsync(
        string url,
        string destination,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".download";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ZenoraRGB/0.2.0");

            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && length > MaxDownloadBytes)
                throw new InvalidOperationException("The runtime download exceeds the safety limit.");

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;

                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidOperationException("The runtime download exceeded the safety limit.");

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var actualHash = await ComputeSha256Async(temporary, cancellationToken);
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Runtime integrity check failed. Expected SHA-256 {expectedSha256}, received {actualHash}.");

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void SafeExtractZip(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > 2500)
            throw new InvalidDataException("The OpenRGB archive contains too many files.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            expandedBytes += entry.Length;
            if (expandedBytes > MaxExpandedBytes)
                throw new InvalidDataException("The OpenRGB archive exceeds the extraction size limit.");

            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The OpenRGB archive contains an unsafe path.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
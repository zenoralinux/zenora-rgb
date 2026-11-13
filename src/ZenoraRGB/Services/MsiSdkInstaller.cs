using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace ZenoraRGB.Services;

/// <summary>
/// Downloads the proprietary MSI SDK directly from MSI to the current user's machine.
/// MSI's SDK is not copied into this repository or redistributed by Zenora RGB.
/// </summary>
public static class MsiSdkInstaller
{
    private static readonly Uri OfficialSdkUri =
        new("https://download.msi.com/uti_exe/Mystic_light_SDK.zip");

    private const long MaxDownloadBytes = 100L * 1024 * 1024;
    private const long MaxExpandedBytes = 150L * 1024 * 1024;

    private static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenoraRGB", "VendorSDK", "MSI");

    public static async Task<string> DownloadAndExtractAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallDirectory);
        var tempZip = Path.Combine(Path.GetTempPath(), $"ZenoraRGB-MSI-SDK-{Guid.NewGuid():N}.zip");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            using var request = new HttpRequestMessage(HttpMethod.Get, OfficialSdkUri);
            request.Headers.UserAgent.ParseAdd("ZenoraRGB/0.1.0 (Windows desktop application)");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long length && length > MaxDownloadBytes)
                throw new InvalidOperationException("The MSI SDK download is larger than the allowed safety limit.");

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = new FileStream(
                tempZip, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxDownloadBytes)
                        throw new InvalidOperationException("The MSI SDK download exceeded the safety limit.");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            using var archive = ZipFile.OpenRead(tempZip);
            if (archive.Entries.Count > 1000)
                throw new InvalidOperationException("The MSI SDK archive contains an unexpected number of files.");

            long expanded = 0;
            var root = Path.GetFullPath(InstallDirectory) + Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries)
            {
                expanded += entry.Length;
                if (expanded > MaxExpandedBytes)
                    throw new InvalidOperationException("The MSI SDK archive exceeds the extraction safety limit.");

                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(InstallDirectory, relative));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The MSI SDK archive contains an unsafe file path.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(output, cancellationToken);
            }

            var dllPath = Directory.GetFiles(
                    InstallDirectory, "MysticLight_SDK_x64.dll", SearchOption.AllDirectories)
                .FirstOrDefault();

            if (dllPath is null)
                throw new FileNotFoundException(
                    "The MSI archive downloaded, but MysticLight_SDK_x64.dll was not found inside it.");

            return dllPath;
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); }
            catch { /* temp file cleanup is best-effort */ }
        }
    }
}

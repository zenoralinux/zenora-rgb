using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace ZenoraRGB.Services;

/// <summary>
/// Extracts all non-.NET prerequisites from resources embedded in ZenoraRGB.exe.
/// Nothing is downloaded at application runtime. Native executables and signed
/// installers are extracted to LocalAppData because Windows cannot launch them
/// directly from a managed assembly resource.
/// </summary>
public static class OpenRgbRuntimeInstaller
{
    public const string OpenRgbVersion = "1.1";
    public const string PawnIoVersion = "2.2.0";

    private const string OpenRgbResource = "ZenoraRGB.Resources.OpenRGB.zip";
    private const string PawnIoResource = "ZenoraRGB.Resources.PawnIO_setup.exe";
    private const string VcRedistResource = "ZenoraRGB.Resources.vc_redist.x64.exe";
    private const string NoticesResource = "ZenoraRGB.Resources.THIRD-PARTY-NOTICES.txt";

    private const string OpenRgbSha256 =
        "63291726C6FC84EB479EF2EE752E97E1187F6CE9C49FD27EEE40DED588A8172A";
    private const string PawnIoSha256 =
        "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const long MaxOpenRgbArchiveBytes = 80L * 1024 * 1024;
    private const long MaxOpenRgbExpandedBytes = 420L * 1024 * 1024;
    private const int MaxOpenRgbEntries = 2500;
    private const long MaxInstallerBytes = 60L * 1024 * 1024;

    private static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenoraRGB", "Runtime");

    private static string OpenRgbDirectory =>
        Path.Combine(RootDirectory, $"OpenRGB-{OpenRgbVersion}");

    private static string PawnIoDirectory =>
        Path.Combine(RootDirectory, $"PawnIO-{PawnIoVersion}");

    public static async Task<string> PreparePrerequisitesAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);

        await EnsureVCRedistAsync(report, cancellationToken);

        try
        {
            var installCode = await InstallPawnIoAsync(report, cancellationToken);
            if (installCode == 3010)
                report?.Invoke("PawnIO was installed. Restart Windows before expecting RAM/SMBus access.");
        }
        catch (Exception ex)
        {
            // Direct USB/PCIe controllers can still be scanned without PawnIO.
            report?.Invoke($"PawnIO is unavailable: {ex.Message} Direct USB/PCIe devices will still be scanned.");
        }

        var executable = await EnsureOpenRgbAsync(report, cancellationToken);
        await ExtractThirdPartyNoticesAsync(cancellationToken);
        return executable;
    }

    public static async Task<string> EnsureOpenRgbAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(RootDirectory);

        var executable = FindOpenRgbExecutable(OpenRgbDirectory);
        if (executable is not null)
            return executable;

        report?.Invoke("Preparing the embedded OpenRGB 1.1 engine...");
        var archivePath = Path.Combine(RootDirectory, $"OpenRGB-{OpenRgbVersion}.zip");
        var stagingDirectory = Path.Combine(
            RootDirectory, $"OpenRGB-{OpenRgbVersion}-{Guid.NewGuid():N}.staging");

        try
        {
            await ExtractEmbeddedResourceAsync(
                OpenRgbResource, archivePath, OpenRgbSha256,
                MaxOpenRgbArchiveBytes, cancellationToken);

            SafeExtractZip(archivePath, stagingDirectory);
            executable = FindOpenRgbExecutable(stagingDirectory);

            if (executable is null)
                throw new FileNotFoundException(
                    "The embedded OpenRGB package is invalid: OpenRGB.exe was not found.");

            if (Directory.Exists(OpenRgbDirectory))
                Directory.Delete(OpenRgbDirectory, recursive: true);

            Directory.Move(stagingDirectory, OpenRgbDirectory);
            executable = FindOpenRgbExecutable(OpenRgbDirectory)
                ?? throw new FileNotFoundException(
                    "OpenRGB.exe disappeared after the embedded runtime was extracted.");

            report?.Invoke($"OpenRGB {OpenRgbVersion} is ready from the embedded package.");
            return executable;
        }
        finally
        {
            TryDelete(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static string? FindOpenRgbExecutable(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.GetFiles(directory, "OpenRGB.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    public static async Task<int> EnsureVCRedistAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (IsVCRedistInstalled())
        {
            report?.Invoke("Microsoft Visual C++ x64 runtime is already installed.");
            return 0;
        }

        report?.Invoke("Installing the embedded Microsoft Visual C++ runtime...");
        var installerPath = Path.Combine(RootDirectory, "vc_redist.x64.exe");

        try
        {
            await ExtractEmbeddedResourceAsync(
                VcRedistResource, installerPath, BundledRuntimeInfo.VcRedistSha256,
                MaxInstallerBytes, cancellationToken);

            var exitCode = await RunInstallerAsync(
                installerPath, "/install /quiet /norestart", cancellationToken);

            if (exitCode is not 0 and not 3010 and not 1638)
                throw new InvalidOperationException(
                    $"Microsoft Visual C++ runtime installation failed with exit code {exitCode}.");

            if (exitCode == 1638 && !IsVCRedistInstalled())
            {
                throw new InvalidOperationException(
                    "A different Visual C++ runtime installer is already running, but the required x64 runtime is not registered.");
            }

            if (exitCode == 3010)
                report?.Invoke("Microsoft Visual C++ runtime was installed; Windows requested a restart.");
            else
                report?.Invoke("Microsoft Visual C++ runtime is ready.");

            return exitCode;
        }
        finally
        {
            TryDelete(installerPath);
        }
    }

    private static bool IsVCRedistInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");

            if (key?.GetValue("Installed") is not int installed || installed != 1)
                return false;

            var major = ReadRegistryInt(key, "Major");
            var minor = ReadRegistryInt(key, "Minor");
            var build = ReadRegistryInt(key, "Bld");

            // The embedded installer is the VS 2026 v14.50 x64 redistributable.
            return major > 14 || (major == 14 && (minor > 50 || (minor == 50 && build >= 35710)));
        }
        catch
        {
            return false;
        }
    }

    private static int ReadRegistryInt(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        return value is int integer ? integer : 0;
    }

    private static bool IsPawnIoInstalled()
    {
        try
        {
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");

            var displayVersion = uninstallKey?.GetValue("DisplayVersion") as string;
            if (Version.TryParse(displayVersion, out var installedVersion))
                return installedVersion >= new Version(2, 2, 0);

            // PawnIO is registered by Windows as a kernel driver service.
            using var serviceKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\PawnIO");
            return serviceKey is not null;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<int> InstallPawnIoAsync(
        Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        if (IsPawnIoInstalled())
        {
            report?.Invoke($"PawnIO {PawnIoVersion} or newer is already installed.");
            return 0;
        }

        report?.Invoke("Preparing the embedded, publisher-signed PawnIO driver installer...");
        Directory.CreateDirectory(PawnIoDirectory);
        var installerPath = Path.Combine(PawnIoDirectory, "PawnIO_setup.exe");

        try
        {
            await ExtractEmbeddedResourceAsync(
                PawnIoResource, installerPath, PawnIoSha256,
                MaxInstallerBytes, cancellationToken);

            report?.Invoke(
                "Installing PawnIO silently. Windows administrator approval was requested by Zenora RGB.");

            var exitCode = await RunInstallerAsync(
                installerPath, "-install -silent", cancellationToken);

            if (exitCode is not 0 and not 3010)
                throw new InvalidOperationException(
                    $"PawnIO installation failed with exit code {exitCode}.");

            if (exitCode == 3010)
            {
                report?.Invoke("PawnIO was installed. Restart Windows to activate the driver.");
                return exitCode;
            }

            if (!IsPawnIoInstalled())
            {
                throw new InvalidOperationException(
                    "The PawnIO installer returned success, but its Windows driver service was not registered.");
            }

            report?.Invoke("PawnIO driver service is installed.");
            return exitCode;
        }
        finally
        {
            TryDelete(installerPath);
        }
    }

    private static async Task<int> RunInstallerAsync(
        string executable,
        string arguments,
        CancellationToken cancellationToken)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };

        using var process = System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException(
                $"Windows could not start {Path.GetFileName(executable)}.");

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { /* best-effort cancellation */ }
        });

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static async Task ExtractEmbeddedResourceAsync(
        string resourceName,
        string destination,
        string expectedSha256,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Required embedded component '{resourceName}' is missing. " +
                "This build was not packaged with its native prerequisites.");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".extract";

        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;

                while (true)
                {
                    var read = await resource.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;

                    total += read;
                    if (total > maximumBytes)
                        throw new InvalidDataException(
                            $"Embedded component '{resourceName}' exceeds its size limit.");

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            var actualHash = await ComputeSha256Async(temporary, cancellationToken);
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Embedded component integrity check failed for {resourceName}. " +
                    $"Expected {expectedSha256}; received {actualHash}.");
            }

            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void SafeExtractZip(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxOpenRgbEntries)
            throw new InvalidDataException("The embedded OpenRGB archive contains too many files.");

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > MaxOpenRgbExpandedBytes)
                throw new InvalidDataException("The embedded OpenRGB archive exceeds its extraction limit.");

            // Reject Unix symbolic links and other special files.
            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixFileType == 0xA000)
                throw new InvalidDataException("The OpenRGB archive contains a symbolic link.");

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

    private static async Task ExtractThirdPartyNoticesAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        await using var resource = assembly.GetManifestResourceStream(NoticesResource);
        if (resource is null)
            return;

        var destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenoraRGB", "THIRD-PARTY-NOTICES.txt");

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.Read,
            4096, useAsync: true);
        await resource.CopyToAsync(output, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* best-effort cleanup */ }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }
}
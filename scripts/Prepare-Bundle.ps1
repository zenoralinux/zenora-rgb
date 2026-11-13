param(
    [string]$BundleDirectory = (Join-Path $PSScriptRoot "..\bundle"),
    [string]$SourceDirectory = (Join-Path $PSScriptRoot "..\third-party-sources")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$bundle = [System.IO.Path]::GetFullPath($BundleDirectory)
$sources = [System.IO.Path]::GetFullPath($SourceDirectory)
New-Item -ItemType Directory -Force -Path $bundle, $sources | Out-Null

function Download-VerifiedFile {
    param([string]$Url, [string]$Destination, [string]$Sha256)
    Write-Host "Downloading $([System.IO.Path]::GetFileName($Destination))..."
    $temporary = "$Destination.download"
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -Uri $Url -OutFile $temporary
    $actual = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        throw "SHA-256 mismatch for $Destination. Expected $Sha256, received $actual."
    }
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
}

$openRgb = Join-Path $bundle "OpenRGB_1.1_Windows_64_8e16de2.zip"
$pawnIo = Join-Path $bundle "PawnIO_setup.exe"
$vcRedist = Join-Path $bundle "vc_redist.x64.exe"

Download-VerifiedFile -Url "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.1/OpenRGB_1.1_Windows_64_8e16de2.zip" -Destination $openRgb -Sha256 "63291726C6FC84EB479EF2EE752E97E1187F6CE9C49FD27EEE40DED588A8172A"
Download-VerifiedFile -Url "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe" -Destination $pawnIo -Sha256 "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032"

# Microsoft Visual Studio 2026 v14 x64 redistributable.
Write-Host "Downloading Microsoft Visual C++ x64 Redistributable..."
Invoke-WebRequest -Uri "https://aka.ms/vs/18/release/vc_redist.x64.exe" -OutFile $vcRedist
$vcSignature = Get-AuthenticodeSignature -FilePath $vcRedist
if ($vcSignature.Status -ne "Valid" -or $vcSignature.SignerCertificate.Subject -notmatch "Microsoft Corporation") {
    throw "The Visual C++ Redistributable does not have a valid Microsoft Authenticode signature."
}
$vcVersion = [version](Get-Item -LiteralPath $vcRedist).VersionInfo.FileVersion
if ($vcVersion -lt [version]"14.50.35710.0") {
    throw "Unexpected Visual C++ Redistributable version $vcVersion; expected 14.50.35710 or newer."
}
$vcHash = (Get-FileHash -LiteralPath $vcRedist -Algorithm SHA256).Hash

# Keep the publisher-signed PawnIO installer byte-for-byte unchanged.
$pawnSignature = Get-AuthenticodeSignature -FilePath $pawnIo
if ($pawnSignature.Status -ne "Valid" -or $pawnSignature.SignerCertificate.Subject -notmatch "namazso") {
    throw "The PawnIO installer signature is not valid or is not from its publisher."
}

# Confirm that the portable OpenRGB package contains the native executable.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($openRgb)
try {
    $openRgbEntry = $archive.Entries | Where-Object { $_.FullName -match '(^|/)OpenRGB\.exe$' } | Select-Object -First 1
    if ($null -eq $openRgbEntry) {
        throw "The OpenRGB portable archive does not contain OpenRGB.exe."
    }
}
finally {
    $archive.Dispose()
}

# Attach corresponding upstream source archives to the GitHub Release.
$openRgbSource = Join-Path $sources "OpenRGB-1.1-source.zip"
$pawnIoSource = Join-Path $sources "PawnIO-2.2.0-source.zip"
Invoke-WebRequest -Uri "https://codeberg.org/OpenRGB/OpenRGB/archive/release_1.1.zip" -OutFile $openRgbSource
Invoke-WebRequest -Uri "https://github.com/namazso/PawnIO/archive/refs/tags/2.2.0.zip" -OutFile $pawnIoSource

$noticeLines = @(
    "ZENORA RGB - THIRD-PARTY COMPONENTS",
    "===================================",
    "",
    "OpenRGB 1.1 Windows x64 portable runtime",
    "Upstream: https://codeberg.org/OpenRGB/OpenRGB",
    "Release: https://codeberg.org/OpenRGB/OpenRGB/releases/tag/release_1.1",
    "License: GPL-2.0-or-later",
    "Binary archive SHA-256: 63291726C6FC84EB479EF2EE752E97E1187F6CE9C49FD27EEE40DED588A8172A",
    "Corresponding source: OpenRGB-1.1-source.zip in this GitHub Release.",
    "The upstream portable archive is embedded and distributed unmodified.",
    "",
    "PawnIO official setup 2.2.0",
    "Upstream installer: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0",
    "Driver source: https://github.com/namazso/PawnIO/tree/2.2.0",
    "Installer SHA-256: 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032",
    "The official signed installer is embedded and distributed unmodified.",
    "The installer states: This installer can be redistributed unmodified.",
    "The driver is GPL-2.0-or-later with the upstream device-interface exception.",
    "Corresponding source: PawnIO-2.2.0-source.zip in this GitHub Release.",
    "",
    "Microsoft Visual C++ v14 x64 Redistributable",
    "Version: $vcVersion",
    "Official download: https://aka.ms/vs/18/release/vc_redist.x64.exe",
    "Publisher: Microsoft Corporation; Authenticode signature checked during packaging.",
    "SHA-256: $vcHash",
    "Microsoft redistributable license terms apply.",
    "",
    "Zenora RGB application",
    "The .NET 10 application is published self-contained as one Windows x64 EXE.",
    "OpenRGB, PawnIO Setup and the Visual C++ Redistributable are embedded resources.",
    "Native components are extracted to LocalAppData because Windows loads native",
    "executables and driver installers from files, not managed assembly resources.",
    "The official PawnIO installer registers its kernel driver service. Windows UAC",
    "approval and, in some cases, a Windows restart are required."
)
$notices = Join-Path $bundle "THIRD-PARTY-NOTICES.txt"
Set-Content -LiteralPath $notices -Value $noticeLines -Encoding utf8

$generatedInfo = Join-Path $repoRoot "src\ZenoraRGB\Services\BundledRuntimeInfo.cs"
@"
namespace ZenoraRGB.Services;

internal static class BundledRuntimeInfo
{
    public const string VcRedistSha256 = "$vcHash";
}
"@ | Set-Content -LiteralPath $generatedInfo -Encoding utf8

$manifest = Join-Path $bundle "BUNDLE-MANIFEST.txt"
@(
    "OpenRGB_1.1_Windows_64_8e16de2.zip  $((Get-FileHash $openRgb -Algorithm SHA256).Hash)",
    "PawnIO_setup.exe  $((Get-FileHash $pawnIo -Algorithm SHA256).Hash)",
    "vc_redist.x64.exe  $vcHash",
    "OpenRGB-1.1-source.zip  $((Get-FileHash $openRgbSource -Algorithm SHA256).Hash)",
    "PawnIO-2.2.0-source.zip  $((Get-FileHash $pawnIoSource -Algorithm SHA256).Hash)"
) | Set-Content -LiteralPath $manifest -Encoding utf8

Write-Host "Embedded prerequisites prepared:"
Write-Host " - OpenRGB 1.1 portable runtime"
Write-Host " - PawnIO 2.2.0 official signed setup"
Write-Host " - Microsoft Visual C++ Redistributable $vcVersion"
Write-Host " - Third-party notices and upstream source archives"

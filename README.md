# Zenora RGB

Zenora RGB is a Windows x64 RGB-control desktop application. Version 0.3.0 is delivered as one self-contained EXE with its native prerequisites embedded.

## Fully embedded package

The single `ZenoraRGB.exe` contains:

- OpenRGB 1.1 Windows x64 portable runtime and its Qt/native runtime files.
- The official, publisher-signed PawnIO Setup 2.2.0 installer.
- Microsoft Visual C++ v14 x64 Redistributable.
- The self-contained .NET 10 desktop runtime.
- Third-party notices and component integrity metadata.

The installer files are embedded as EXE resources and SHA-256 verified before they are extracted or executed. The OpenRGB archive is distributed unmodified. The PawnIO installer is also distributed byte-for-byte unchanged so that its publisher signature remains valid. The build pipeline verifies both upstream hashes and the Authenticode signatures of PawnIO and Microsoft's redistributable.

**No internet connection is needed on the user's PC after downloading ZenoraRGB.exe.** The build pipeline downloads the upstream components once, verifies them, embeds them and publishes the resulting EXE.

## First run

1. Download `ZenoraRGB.exe` from the v0.3.0 GitHub Release.
2. Run it and approve the Windows administrator prompt.
3. Zenora RGB prepares the embedded Visual C++ runtime, installs PawnIO if it is not already present, extracts OpenRGB locally, and starts the local OpenRGB server.
4. The application scans for supported RGB controllers automatically.

The app does not need Armoury Crate, ASUS Aura, MSI Center or Mystic Light. The official PawnIO installer registers its kernel driver service. A Windows restart may be required after driver installation.

## Why files are extracted after launch

The prerequisites are inside the one EXE. Windows does not load a native child EXE or a kernel-driver installer directly from a managed assembly resource, so Zenora RGB extracts the original native packages to:

`%LOCALAPPDATA%\ZenoraRGB\Runtime`

OpenRGB remains cached there for later launches. The PawnIO and Visual C++ installers are removed after their installation steps. This does not create a second app installation folder next to ZenoraRGB.exe.

The OpenRGB SDK server runs as a hidden child process bound only to `127.0.0.1:6742` while Zenora RGB is open. It is stopped when the application closes. PawnIO is a separate Windows kernel driver service installed by its official installer and remains installed so other supported hardware tools can use it too.

## Windows permissions and safety

- The app requests Administrator privileges because low-level driver installation and hardware access require elevation.
- UAC approval is never bypassed.
- PawnIO is installed from its official signed installer with `-install -silent`; Zenora RGB does not modify the installer or driver.
- OpenRGB is extracted from the SHA-256-verified archive.
- Microsoft Visual C++ Redistributable is checked against its embedded SHA-256 and its Microsoft Authenticode signature is verified during packaging.
- Close other RGB-control applications if they are already controlling the same hardware.

## Hardware compatibility

OpenRGB supports many RGB controllers, but compatibility depends on the exact controller and firmware revision. A motherboard, GPU, RAM kit or case is only controllable if OpenRGB supports its actual controller.

The desktop configuration used for target testing is:
- ASUS PRIME Z790-P WiFi motherboard
- Corsair Vengeance RGB memory (exact hardware revision not yet verified)
- MSI GeForce RTX 3090 Suprim X
- Lian Li O11 Dynamic EVO RGB case

Physical detection and color control of those exact components still need to be verified on the target PC. The app reports what OpenRGB actually detects; it does not assume every connected RGB component is supported.

## Build from source

On Windows with the .NET 10 SDK and PowerShell:

1. Prepare the verified third-party components:

```powershell
.\scripts\Prepare-Bundle.ps1
```

2. Publish the single-file EXE:

```powershell
$bundle = (Resolve-Path .\bundle).Path

dotnet publish .\src\ZenoraRGB\ZenoraRGB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:RequireBundledPrerequisites=true "-p:OpenRgbRuntimePath=$bundle\OpenRGB_1.1_Windows_64_8e16de2.zip" "-p:PawnIoInstallerPath=$bundle\PawnIO_setup.exe" "-p:VcRedistPath=$bundle\vc_redist.x64.exe" "-p:ThirdPartyNoticesPath=$bundle\THIRD-PARTY-NOTICES.txt" -o .\publish
```

The result is `publish\ZenoraRGB.exe`. The GitHub Actions workflow performs these same steps and attaches the corresponding OpenRGB and PawnIO source archives plus third-party notices to the GitHub Release.

## Third-party components and source

The v0.3.0 GitHub Release includes:
- OpenRGB 1.1 source archive (GPL-2.0-or-later).
- PawnIO 2.2.0 source archive and its pinned PawnPP submodule source (GPL-2.0-or-later with the upstream device-interface exception).
- Third-party notices and the exact package SHA-256 manifest.

The PawnIO Setup repository hosts the official installer releases; the official installer states that it may be redistributed unmodified. The setup is embedded without alteration so its Authenticode signature remains intact.

Upstream projects:
- OpenRGB: https://codeberg.org/OpenRGB/OpenRGB
- OpenRGB release 1.1: https://codeberg.org/OpenRGB/OpenRGB/releases/tag/release_1.1
- PawnIO Setup: https://github.com/namazso/PawnIO.Setup
- PawnIO source: https://github.com/namazso/PawnIO/tree/2.2.0
- Microsoft Visual C++ Redistributable: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist

## Current scope

This release provides static color control for detected OpenRGB controllers. Animated effects, per-zone mode editing, fan speed, temperature monitoring and per-device profiles are not implemented in Zenora RGB yet. Their availability depends on OpenRGB's support for the relevant controller.

# Zenora RGB

Zenora RGB is a Windows x64 desktop app that controls compatible RGB hardware through a single, vendor-independent OpenRGB engine.

## What changed in 0.2.0

- One unified OpenRGB engine for supported ASUS, MSI, Corsair and other RGB controllers.
- No Armoury Crate, ASUS Aura app, MSI Center, or Mystic Light installation is required.
- First-run setup downloads OpenRGB 1.1 from the separate Zenora GitHub runtime release, verifies its SHA-256, and extracts it automatically.
- When Windows hardware access requires PawnIO, the app downloads the setup from the official PawnIO GitHub release, verifies its SHA-256, and runs the installer silently.
- Local OpenRGB SDK server is bound to 127.0.0.1 only; the app does not expose the control port to the network.
- One HEX palette, presets, selected-device and all-device color application.
- Self-contained .NET runtime. No separate .NET installation is required.

## First run

1. Download and run ZenoraRGB.exe from the latest Zenora RGB GitHub Release.
2. Approve the Windows administrator prompt. The app needs this permission to install the hardware-access driver and communicate with some motherboard/RAM controllers.
3. Zenora RGB automatically downloads and prepares OpenRGB, checks/installs PawnIO when needed, starts the local engine, and scans your devices.
4. Select a detected controller, choose a color, then apply it to the selected controller or all detected controllers. The Connect and scan button remains available for another scan.

No manual SDK selection or vendor RGB software installation is required. A Windows restart may be needed after PawnIO installation.

## Runtime downloads and integrity

OpenRGB is an independent GPL-licensed upstream project. Zenora RGB rehosts the unmodified OpenRGB 1.1 Windows x64 portable archive in the separate runtime-v1.1 GitHub Release, alongside the corresponding upstream source archive and third-party notices. The app verifies the pinned SHA-256 before extraction.

PawnIO is not redistributed by Zenora RGB. It is retrieved from the official upstream GitHub release and SHA-256-verified before the silent installer is launched.

## Hardware compatibility

OpenRGB supports a broad range of RGB controllers, but support is model- and controller-specific. A device can appear with limited control, or not be detected at all. OpenRGB's supported-device list and known issue tracker are the source of truth for compatibility.

The known desktop configuration this project is being tested against includes:
- ASUS PRIME Z790-P WiFi motherboard
- Corsair Vengeance RGB memory (exact hardware revision not yet verified)
- MSI GeForce RTX 3090 Suprim X
- Lian Li O11 Dynamic EVO RGB case

Detection and control of those exact components still require a real test on the target PC. The app must not be assumed to control a case, fan hub, or RGB strip merely because it is connected to the motherboard.

## Important Windows notes

- The app requests administrator privileges at startup. Windows UAC approval cannot and should not be bypassed.
- Some devices need PawnIO and/or a Windows restart before they can be accessed.
- Close other RGB utilities before connecting. Competing services may hold exclusive access to a controller.
- Do not install or operate multiple low-level RGB tools against the same controller at the same time.
- The OpenRGB SDK endpoint is only bound to loopback (127.0.0.1) and is not intended for remote access.

## Build from source

Install the .NET 10 SDK on Windows, then run:

    dotnet publish .\src\ZenoraRGB\ZenoraRGB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish

The output is publish\ZenoraRGB.exe.

## Upstream projects

- OpenRGB: https://codeberg.org/OpenRGB/OpenRGB
- OpenRGB release 1.1: https://codeberg.org/OpenRGB/OpenRGB/releases/tag/release_1.1
- PawnIO Setup: https://github.com/namazso/PawnIO.Setup
- OpenRGB supported devices: https://openrgb.org/devices.html

## Scope

This release provides static color control. Animated effects, per-zone mode editing, fan speed control, temperature monitoring, and per-device profiles are not implemented in Zenora RGB yet. Their availability depends on a future app release and upstream hardware support.

## Safety

The app does not copy or redistribute ASUS/MSI proprietary SDKs. It uses OpenRGB's supported controller drivers and PawnIO only when required. All downloaded runtime files are integrity-checked before use.
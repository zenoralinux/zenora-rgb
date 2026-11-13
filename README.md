# Zenora RGB

A Windows x64 desktop prototype for controlling ASUS Aura and MSI Mystic Light from one simple interface.

## Current prototype features

- ASUS Aura SDK v3.1 discovery and static color control through the registered COM API.
- MSI Mystic Light SDK discovery and static color control through the official x64 native SDK DLL.
- One HEX color palette with quick presets.
- Apply a color to all discovered ASUS devices, one selected MSI LED zone, or both connected vendor stacks.
- Local settings for the selected MSI SDK DLL path.
- No network account, telemetry, or cloud service.

## Requirements

### ASUS

1. Install ASUS Armoury Crate and the ASUS Lighting Service / Aura components for your motherboard.
2. The app looks for the registered Aura SDK COM ProgID `aura.sdk.1`.
3. If the Aura SDK COM class is not registered, repair/update Armoury Crate and its Lighting Service components, then reboot.

The ASUS SDK guide documents the COM flow used here: `SwitchMode()`, `Enumerate(0)`, setting each `IAuraRgbLight.Color`, and calling `Apply()`.

### MSI

1. Install MSI Center and its Mystic Light component.
2. Download the official Mystic Light SDK from [MSI's developer page](https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download).
3. Extract the SDK ZIP and select `MysticLight_SDK_x64.dll` in Zenora RGB.
4. Click **Connect MSI**.

The MSI SDK is proprietary/vendor-supplied and is not redistributed in this repository or release. The selected DLL path is saved under your local AppData folder.

## Build from source

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet publish .\src\ZenoraRGB\ZenoraRGB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The output is `publish\ZenoraRGB.exe`.

## GitHub Actions release

The workflow builds a self-contained, single-file Windows x64 executable on pushes and pull requests. To publish a GitHub Release, push a version tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

The workflow attaches `ZenoraRGB.exe` to the matching GitHub Release.

## Scope and limitations of 0.1.0

- This is a hardware-control prototype, not a replacement for all Armoury Crate or MSI Center features.
- ASUS control requires an installed and registered Aura SDK COM component. It enumerates all Aura SDK devices and applies one static color to all their LEDs.
- MSI control requires MSI's official 64-bit SDK DLL plus the MSI Mystic Light application stack. It applies a static color to a selected LED area.
- Animated effects, fan speed, temperature monitoring, device-specific ASUS selection, Corsair/iCUE, and non-ASUS/MSI adapters are not implemented yet.
- Some vendor SDK versions or devices may reject third-party control. The app reports SDK errors instead of silently pretending a command succeeded.
- Do not run competing RGB controllers against the same device at the same time. Release control before handing the device back to Armoury Crate or MSI Center.

## Safety

The app does not write directly to raw SMBus/I²C registers and does not install kernel drivers. It uses vendor-provided interfaces. The MSI SDK binary is loaded only from the path explicitly selected by the user.

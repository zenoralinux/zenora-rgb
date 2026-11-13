# Zenora RGB

A Windows x64 desktop prototype for controlling ASUS Aura and MSI Mystic Light from one interface.

## Current prototype features

- ASUS Aura SDK discovery and static color control through the registered COM API.
- MSI Mystic Light SDK discovery and static color control through the official x64 native SDK.
- One-click download and local extraction of MSI's SDK from MSI's official download host.
- One HEX color palette with quick presets.
- Apply a color to all discovered ASUS devices, one selected MSI LED zone, or both connected vendor stacks.
- Local settings for the MSI SDK path.
- Self-contained .NET runtime in the single-file EXE. No separate .NET installation is required.
- No Zenora account, telemetry, or cloud service.

## Vendor components

### ASUS

Install Armoury Crate and the ASUS Lighting Service / Aura components for your motherboard. Zenora RGB looks for the registered Aura SDK COM ProgID `aura.sdk.1`. If the COM component is absent, repair the ASUS lighting components and reboot.

ASUS developer references:
- https://www.asus.com/campaign/aura/us/SDK.php
- https://www.asus.com/campaign/aura/download/ASUS_Aura_SDK_FAQ.pdf

ASUS Lighting Service / Aura SDK components remain vendor-installed prerequisites; ASUS binaries are not copied into Zenora RGB's public release.

### MSI

Install MSI Center and its Mystic Light component. In Zenora RGB, click **Download official SDK**. The app downloads the official MSI SDK archive directly from MSI and extracts it to:

`%LOCALAPPDATA%\ZenoraRGB\VendorSDK\MSI`

Then click **Connect MSI**. The SDK download is performed on the user's PC; the proprietary MSI SDK is not included in this repository or the GitHub Release.

Official MSI developer page:
https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download

The MSI SDK documentation marks the material Confidential. Zenora RGB therefore does not redistribute the SDK DLL. MSI's own Mystic Light application stack must still be installed.

### If the automatic SDK download is unavailable

Use **Choose DLL** after downloading the x64 package from MSI's official developer page. Select `MysticLight_SDK_x64.dll`.

## Build from source

Install the .NET 10 SDK on Windows, then run:

```powershell
dotnet publish .\src\ZenoraRGB\ZenoraRGB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

The output is `publish\ZenoraRGB.exe`.

## GitHub Releases

Every successful push to `main` creates or refreshes the `v0.1.0` GitHub Release asset. Pushing a version tag such as `v0.2.0` creates a separately versioned release.

The release contains Zenora RGB only. Vendor SDKs are fetched from their original manufacturers on the user's machine, not rehosted by this project.

## Scope and limitations of 0.1.0

- This is a hardware-control prototype, not a full replacement for Armoury Crate or MSI Center.
- ASUS control requires an installed and registered Aura SDK COM component. It enumerates Aura SDK devices and applies one static color to all their LEDs.
- MSI control requires MSI's official 64-bit SDK and MSI Mystic Light application stack. It applies a static color to a selected LED area.
- Animated effects, fan speed, temperature monitoring, Corsair/iCUE, and non-ASUS/MSI adapters are not implemented yet.
- Device compatibility depends on the installed vendor service, SDK version, and exact hardware.
- Do not run competing RGB controllers against the same device at the same time. Release control before handing a device back to the vendor application.

## Safety

The app does not write directly to raw SMBus/I²C registers and does not install kernel drivers. Vendor SDKs are loaded only from vendor-installed components or the locally downloaded MSI SDK.

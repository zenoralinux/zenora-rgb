# Zenora RGB architecture notes

## Control engine

Zenora RGB uses the OpenRGB 1.1 Windows x64 portable runtime as its only RGB hardware backend. The app downloads the unmodified runtime from the separate GitHub Release tagged runtime-v1.1, verifies its pinned SHA-256, and extracts it under the current user's LocalAppData directory.

No ASUS Aura COM SDK, MSI Mystic Light SDK, Armoury Crate, MSI Center, or Mystic Light runtime is loaded or required by Zenora RGB.

## Local SDK bridge

The app starts the OpenRGB SDK server with a loopback-only listener at 127.0.0.1:6742, then uses the OpenRGB protocol v6 client implemented in OpenRgbSdkClient.cs. The listener is not exposed to the LAN.

The client negotiates protocol version 6, requests RGBController plus localhost-client flags, reads the controller count and stable unique controller IDs, loads each controller description by its unique ID, and submits RGBColor arrays using NET_PACKET_ID_RGBCONTROLLER_UPDATELEDS. Controller descriptions are parsed using the protocol-v6 fields (including alternate LED names, display name, configuration data, zone modes, and v6 segment metadata).

The parser contains a compatibility retry for protocol-v6 zone color-order fields, because this field has appeared in protocol-v6 SDK documentation variants. Changes to the upstream SDK schema must be reviewed before moving OpenRGB to another major version.

## Windows hardware access

OpenRGB 1.1 uses PawnIO on Windows for SMBus/I2C access needed by many RAM modules, some motherboard RGB controllers, and some graphics-card controllers. Zenora RGB downloads PawnIO Setup 2.2.0 directly from its upstream GitHub release, checks the SHA-256, and runs the upstream silent installer from the elevated app process.

The app manifest requests administrator privileges. This is intentional: Windows UAC consent is required for low-level driver installation and privileged hardware access. No UAC bypass is attempted.

PawnIO's installer is not redistributed in Zenora RGB's releases. The OpenRGB portable archive is redistributed separately with the corresponding upstream source archive and third-party notice.

## Release pipeline

- The OpenRGB runtime workflow downloads the official upstream 1.1 Windows x64 portable archive from Codeberg.
- The workflow verifies the upstream SHA-256 before attaching the archive to runtime-v1.1.
- The same runtime release includes OpenRGB's corresponding source archive and third-party notices.
- The main application release waits until the runtime archive is published, then publishes the self-contained ZenoraRGB.exe as v0.2.0.
- The Windows app verifies the runtime SHA-256 again before extraction.

## Compatibility limits

OpenRGB's supported-device list is controller- and revision-specific. A motherboard, graphics card, RAM kit, fan hub, or case is only controllable if OpenRGB supports the actual controller and firmware revision. The application must report what OpenRGB detects instead of assuming every component is supported.

## Sources

- OpenRGB release 1.1: https://codeberg.org/OpenRGB/OpenRGB/releases/tag/release_1.1
- OpenRGB SDK: https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md
- OpenRGB supported devices: https://openrgb.org/devices.html
- PawnIO setup: https://github.com/namazso/PawnIO.Setup

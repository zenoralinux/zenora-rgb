# Zenora RGB architecture and bundled components

## Single-file packaging

Version 0.3.0 is published as one self-contained Windows x64 executable. The .NET 10 runtime, WPF application, and these native packages are embedded resources:

- OpenRGB 1.1 Windows x64 portable archive.
- Official signed PawnIO Setup 2.2.0.
- Microsoft Visual C++ v14 x64 Redistributable.
- Third-party notices.

The CI job prepares the binaries using `scripts/Prepare-Bundle.ps1`, verifies fixed SHA-256 hashes for OpenRGB and PawnIO, verifies Authenticode signatures for PawnIO and Microsoft's redistributable, writes the Microsoft redistributable hash into BundledRuntimeInfo.cs, and passes the three binary paths to MSBuild as EmbeddedResource items. The final app build fails if a required component is absent.

At runtime, the app verifies the embedded hashes again. Native packages are extracted under `%LOCALAPPDATA%\ZenoraRGB\Runtime` because Windows native process loading and driver installation require ordinary filesystem paths. OpenRGB is cached; setup executables are deleted after installation. No runtime HTTP request is made.

## Driver and process lifecycle

- The application manifest requests Administrator privileges. This is needed to install and use the low-level hardware driver; UAC is not bypassed.
- PawnIO Setup runs unchanged with `-install -silent`. Its kernel driver registers the Windows PawnIO driver service and remains installed after Zenora RGB closes.
- OpenRGB is started as a hidden child process with `--server --server-host 127.0.0.1 --server-port 6742`. This server is bound to loopback only and is stopped when Zenora RGB exits; it is not installed as a Windows service.
- The app connects using OpenRGB SDK protocol v6, requests RGBController and local-client flags, reads stable controller IDs, and submits LED updates.

## Third-party licensing

- OpenRGB 1.1 is GPL-2.0-or-later. Its corresponding source archive is attached to the v0.3.0 Release.
- PawnIO driver source is GPL-2.0-or-later with its upstream device-interface exception. The 2.2.0 source archive and its pinned PawnPP submodule source (commit e64e4c37b2d8ba0d8ee57205faf8183aee12c438) are attached to the v0.3.0 Release. The official PawnIO Setup binary is embedded unchanged; the installer states it may be redistributed unmodified.
- Microsoft Visual C++ v14 x64 Redistributable is obtained from Microsoft's official permanent download URL; packaging validates its Microsoft Authenticode signature and the final binary hash.
- Third-party notices and the exact package hash manifest accompany the release and are also embedded in the EXE.

OpenRGB and PawnIO are separate child programs/driver components; Zenora RGB does not statically or dynamically link their native code into its own application assembly.

## Compatibility limits

OpenRGB support depends on exact device controller and firmware. Detection and hardware control for the target PC's exact motherboard, RAM, GPU and case must be verified on that PC.

## Upstream references

- OpenRGB release 1.1: https://codeberg.org/OpenRGB/OpenRGB/releases/tag/release_1.1
- OpenRGB SDK: https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md
- PawnIO Setup 2.2.0: https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0
- PawnIO source 2.2.0: https://github.com/namazso/PawnIO/tree/2.2.0
- Microsoft supported VC++ redistributables: https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist

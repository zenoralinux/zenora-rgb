# Vendor SDK notes

## ASUS Aura

Official references:
- https://www.asus.com/microsite/aurareadydevportal/index.html
- https://www.asus.com/microsite/aurareadydevportal/tutorial_csharp.html
- https://www.asus.com/microsite/aurareadydevportal/installation.html

Aura SDK v3.1 is COM-based. The implementation uses the registered ProgID `aura.sdk.1` and the documented enumeration / color / apply sequence. The ASUS guide says SDK v3.02.04+ is distributed as part of the Lighting Service library.

## MSI Mystic Light

Official references:
- https://www.msi.com/Landing/mystic-light-rgb-gaming-pc/download
- https://storage-asset.msi.com/files/pdf/Mystic_Light_Software_Development_Kit.pdf

The MSI guide requires the Mystic Light related application to be installed. Its SDK exposes `MLAPI_Initialize`, `MLAPI_GetDeviceInfo`, `MLAPI_SetLedColor`, and `MLAPI_Release`. The SDK binary is not vendored because it is a proprietary MSI package.

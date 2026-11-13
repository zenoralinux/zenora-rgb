namespace ZenoraRGB.Models;

public sealed record RgbDevice(
    string Provider,
    string DisplayName,
    string DeviceType,
    uint ZoneIndex,
    string Details);

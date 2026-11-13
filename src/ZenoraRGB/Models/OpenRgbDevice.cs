namespace ZenoraRGB.Models;

public sealed record OpenRgbDevice(
    uint Id,
    string Name,
    string Vendor,
    int LedCount,
    int ZoneCount,
    int DeviceType)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Vendor)
        ? $"{Name} · {LedCount} LEDs"
        : $"{Vendor} · {Name} · {LedCount} LEDs";
}
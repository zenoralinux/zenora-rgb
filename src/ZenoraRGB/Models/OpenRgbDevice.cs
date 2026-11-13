namespace ZenoraRGB.Models;

public sealed record OpenRgbDevice(
    uint Id,
    string Name,
    string Vendor,
    int LedCount,
    int ZoneCount)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Vendor)
        ? $"{Name} · {LedCount} LEDs"
        : $"{Vendor} · {Name} · {LedCount} LEDs";
}
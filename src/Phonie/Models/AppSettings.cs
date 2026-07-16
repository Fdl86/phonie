namespace Phonie.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";

    public string? InputDeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public int PttVirtualKey { get; set; } = 0xA3; // Right Control
}

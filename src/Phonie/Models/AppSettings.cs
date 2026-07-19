namespace Phonie.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";

    public string? InputDeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public int MicrophoneGainDb { get; set; } = 6;

    public int PttVirtualKey { get; set; } = 0xA3; // Right Control

    public string? JoystickPttDeviceKey { get; set; }

    public string? JoystickPttDeviceName { get; set; }

    public int? JoystickPttButton { get; set; }

    public string WhisperModel { get; set; } = "small-q5_1";

    public bool AutoTranscribePtt { get; set; } = true;

    public string PreferredAirportIcao { get; set; } = "LFBI";
}

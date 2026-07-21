namespace Phonie.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";

    public string? InputDeviceId { get; set; }

    public string? OutputDeviceId { get; set; }

    public int MicrophoneGainDb { get; set; } = 9;

    public int PttVirtualKey { get; set; } = 0xA3; // Right Control

    public string? JoystickPttDeviceKey { get; set; }

    public string? JoystickPttDeviceName { get; set; }

    public int? JoystickPttButton { get; set; }

    // Conservé pour migration des réglages DEV0.3.0.1.
    public string WhisperModel { get; set; } = "small-q5_1";

    public string SpeechRecognitionProfile { get; set; } = nameof(global::Phonie.Models.SpeechRecognitionProfile.WhisperSmallCpu);

    public bool AutoTranscribePtt { get; set; } = true;

    public string PreferredAirportIcao { get; set; } = "LFBI";

    public bool AutoUpdateFranceRadioData { get; set; } = true;

    public DateTimeOffset? LastFranceRadioDataCheckUtc { get; set; }
}

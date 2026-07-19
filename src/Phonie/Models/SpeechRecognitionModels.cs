namespace Phonie.Models;

public enum SpeechRecognitionProfile
{
    WhisperBaseCpu,
    WhisperSmallCpu,
    WhisperSmallVulkan,
    WhisperLargeV3TurboVulkan,
    VoskFrench,
}

public enum SpeechRecognitionBackend
{
    Whisper,
    Vosk,
}

public sealed record SpeechRecognitionProfileDefinition(
    SpeechRecognitionProfile Profile,
    string DisplayName,
    string ShortName,
    SpeechRecognitionBackend Backend,
    bool UsesVulkan,
    string Description);

public static class SpeechRecognitionProfiles
{
    public static IReadOnlyList<SpeechRecognitionProfileDefinition> All { get; } =
    [
        new(
            SpeechRecognitionProfile.WhisperBaseCpu,
            "Whisper Base CPU - rapide",
            "Whisper Base CPU",
            SpeechRecognitionBackend.Whisper,
            false,
            "Modèle léger pour réduire la latence."),
        new(
            SpeechRecognitionProfile.WhisperSmallCpu,
            "Whisper Small CPU - équilibré",
            "Whisper Small CPU",
            SpeechRecognitionBackend.Whisper,
            false,
            "Profil de précision CPU actuel."),
        new(
            SpeechRecognitionProfile.WhisperSmallVulkan,
            "Whisper Small Vulkan - GPU",
            "Whisper Small Vulkan",
            SpeechRecognitionBackend.Whisper,
            true,
            "Accélération GPU Vulkan légère avec retour explicite au CPU en cas d'indisponibilité."),
        new(
            SpeechRecognitionProfile.WhisperLargeV3TurboVulkan,
            "Whisper Large-v3 Turbo Vulkan - qualité",
            "Whisper Large-v3 Turbo Vulkan",
            SpeechRecognitionBackend.Whisper,
            true,
            "Profil qualité GPU pour améliorer la transcription des phrases ATC et des indicatifs."),
        new(
            SpeechRecognitionProfile.VoskFrench,
            "Vosk FR - expérimental",
            "Vosk FR",
            SpeechRecognitionBackend.Vosk,
            false,
            "Moteur très rapide réservé aux comparaisons et tests."),
    ];

    public static SpeechRecognitionProfileDefinition Get(SpeechRecognitionProfile profile) =>
        All.First(item => item.Profile == profile);

    public static SpeechRecognitionProfile Parse(string? value)
    {
        return Enum.TryParse<SpeechRecognitionProfile>(value, true, out var profile)
            ? profile
            : SpeechRecognitionProfile.WhisperSmallCpu;
    }
}

public enum SpeechModelState
{
    Missing,
    Downloading,
    Ready,
    Loading,
    Transcribing,
    RestartRequired,
    Error,
}

public sealed record SpeechModelStatus(
    SpeechRecognitionProfile Profile,
    SpeechModelState State,
    string Message,
    double ProgressPercent = 0,
    long DownloadedBytes = 0,
    long? TotalBytes = null);

public sealed record SpeechTranscriptionResult(
    SpeechRecognitionProfile Profile,
    string RawText,
    string NormalizedText,
    TimeSpan ProcessingTime,
    string ModelName,
    string Language,
    IReadOnlyList<string> Segments);

public sealed record SpeechComparisonResult(
    SpeechRecognitionProfile Profile,
    bool WasRun,
    string Transcript,
    TimeSpan ProcessingTime,
    string Status);

public sealed record PilotMessageAnalysis(
    string RawText,
    string NormalizedText,
    string? CalledStation,
    string? Callsign,
    string? ExpectedCallsign,
    string CallsignSource,
    double CallsignConfidence,
    string? Position,
    string? Intention,
    string? AtisLetter,
    bool IsFirstContact,
    IReadOnlyList<string> MissingCriticalFields,
    double Confidence);

public sealed record RadioExchange(
    DateTimeOffset Timestamp,
    PilotMessageAnalysis Analysis,
    string ControllerResponse,
    TimeSpan ProcessingTime,
    bool FromMicrophone);

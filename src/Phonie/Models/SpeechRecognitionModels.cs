namespace Phonie.Models;

public enum SpeechModelState
{
    Missing,
    Downloading,
    Ready,
    Loading,
    Transcribing,
    Error,
}

public sealed record SpeechModelStatus(
    SpeechModelState State,
    string Message,
    double ProgressPercent = 0,
    long DownloadedBytes = 0,
    long? TotalBytes = null);

public sealed record SpeechTranscriptionResult(
    string RawText,
    string NormalizedText,
    TimeSpan ProcessingTime,
    string ModelName,
    string Language,
    IReadOnlyList<string> Segments);

public sealed record PilotMessageAnalysis(
    string RawText,
    string NormalizedText,
    string? CalledStation,
    string? Callsign,
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

namespace Phonie.Models;

public enum OperationalRadioKind
{
    Unknown,
    Controlled,
    InformationService,
    AutomaticBroadcast,
    RecordedMessage,
    SelfInformation,
}

public sealed record OperationalFrequency(
    double FrequencyMhz,
    string ServiceName,
    OperationalRadioKind Kind,
    bool DialogueAllowed,
    string Guidance,
    string Source,
    bool IsDuplicate = false);

public sealed record AtisInformation(
    string AirportIcao,
    string AirportName,
    string Letter,
    string Runway,
    double WindDirectionDegrees,
    double WindSpeedKnots,
    double QnhHpa,
    double TemperatureCelsius,
    double VisibilityMeters,
    string Text,
    DateTimeOffset GeneratedAt,
    string Signature);

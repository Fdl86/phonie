using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonie.Models;


public static class SiaRadioManifestJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

public sealed class SiaRadioManifest
{
    public int SchemaVersion { get; set; } = 2;
    public string DatasetId { get; set; } = "phonie-france-radio-sia";
    public string DatasetRevision { get; set; } = string.Empty;
    public string Authority { get; set; } = "SIA";
    public DateTimeOffset GeneratedAt { get; set; }
    public string GeneratorVersion { get; set; } = string.Empty;
    public string SourceCatalogUrl { get; set; } = string.Empty;
    public bool BootstrapRequired { get; set; }
    public SiaRadioDatasetDescriptor? Previous { get; set; }
    public SiaRadioDatasetDescriptor? Current { get; set; }
    public SiaRadioDatasetDescriptor? Next { get; set; }
}

public sealed class SiaRadioDatasetDescriptor
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string AiracCycle { get; set; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveUntil { get; set; }
    public int AirportCount { get; set; }
    public int FrequencyCount { get; set; }
    public int InteractiveCount { get; set; }
    public int SilentCount { get; set; }
}

public sealed record SiaRadioDatabaseStatus(
    bool Available,
    bool Valid,
    string State,
    string Revision,
    string AiracCycle,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset EffectiveUntil,
    DateTimeOffset GeneratedAt,
    int AirportCount,
    int FrequencyCount,
    string ActivePath,
    string Source,
    string Message,
    string? NextAiracCycle = null,
    DateTimeOffset? NextEffectiveFrom = null);

public sealed record SiaRadioUpdateResult(
    bool Success,
    bool Changed,
    string Message,
    SiaRadioDatabaseStatus Status);

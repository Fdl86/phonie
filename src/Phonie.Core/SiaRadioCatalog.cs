using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Phonie.Core;

public enum SiaRadioServiceKind
{
    Unknown,
    Tower,
    Ground,
    Clearance,
    Approach,
    Departure,
    Information,
    FlightInformation,
    SelfInformation,
    AutomaticBroadcast,
    RecordedMessage,
    ControlledOther,
}

public enum SiaRadioStationScope
{
    Local,
    Regional,
}

public enum SiaRadioScheduleState
{
    Always,
    PublishedNotEvaluated,
    NotApplicable,
}

public sealed class SiaRadioDataset
{
    public int SchemaVersion { get; set; } = 2;
    public string DatasetId { get; set; } = "phonie-france-radio-sia";
    public string Revision { get; set; } = string.Empty;
    public string Authority { get; set; } = "SIA";
    public string SourceKind { get; set; } = string.Empty;
    public string AiracCycle { get; set; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset EffectiveUntil { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public string GeneratorVersion { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public List<SiaAirportRadioRecord> Airports { get; set; } = new();
}

public sealed class SiaAirportRadioRecord
{
    public string Icao { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string SourceReference { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public List<SiaRadioFrequencyRecord> Frequencies { get; set; } = new();
}

public sealed class SiaRadioFrequencyRecord
{
    public string Channel { get; set; } = string.Empty;
    public int ChannelKhz { get; set; }
    public long CarrierHz { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string Callsign { get; set; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SiaRadioServiceKind Kind { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SiaRadioStationScope Scope { get; set; }
    public bool Interactive { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SiaRadioScheduleState ScheduleState { get; set; }
    public string HoursText { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceRecordId { get; set; } = string.Empty;
    public double Confidence { get; set; } = 1.0;
}

public sealed record SiaRadioResolution(
    bool AirportKnown,
    bool FrequencyKnown,
    bool Ambiguous,
    SiaAirportRadioRecord? Airport,
    SiaRadioFrequencyRecord? Frequency,
    IReadOnlyList<SiaRadioFrequencyRecord> Candidates,
    string Reason);

public static class RadioChannel
{
    public static int ToChannelKhz(double valueMhz)
    {
        if (!double.IsFinite(valueMhz) || valueMhz < 100 || valueMhz > 200)
        {
            return 0;
        }

        return checked((int)Math.Round(valueMhz * 1000d, MidpointRounding.AwayFromZero));
    }

    public static string FormatChannel(int channelKhz) =>
        channelKhz <= 0
            ? string.Empty
            : (channelKhz / 1000d).ToString("F3", CultureInfo.InvariantCulture);

    public static long CarrierHzFromChannelKhz(int channelKhz)
    {
        if (channelKhz <= 0)
        {
            return 0;
        }

        var mhz = channelKhz / 1000;
        var khzWithinMhz = channelKhz % 1000;
        var hundredKhz = (khzWithinMhz / 100) * 100;
        var channelRemainder = khzWithinMhz - hundredKhz;
        var carrierOffsetHz = channelRemainder switch
        {
            0 => 0,
            5 => 0,
            10 => 8_333,
            15 => 16_667,
            25 => 25_000,
            30 => 25_000,
            35 => 33_333,
            40 => 41_667,
            50 => 50_000,
            55 => 50_000,
            60 => 58_333,
            65 => 66_667,
            75 => 75_000,
            80 => 75_000,
            85 => 83_333,
            90 => 91_667,
            _ => channelRemainder * 1000,
        };
        return (mhz * 1_000_000L) + (hundredKhz * 1000L) + carrierOffsetHz;
    }

    public static bool Matches(SiaRadioFrequencyRecord record, double valueMhz)
    {
        var activeKhz = ToChannelKhz(valueMhz);
        if (activeKhz <= 0)
        {
            return false;
        }

        var publishedKhz = record.ChannelKhz > 0
            ? record.ChannelKhz
            : ParseChannelKhz(record.Channel);
        if (publishedKhz == activeKhz)
        {
            return true;
        }

        var activeCarrier = CarrierHzFromChannelKhz(activeKhz);
        var publishedCarrier = record.CarrierHz > 0
            ? record.CarrierHz
            : CarrierHzFromChannelKhz(publishedKhz);
        return activeCarrier > 0
            && publishedCarrier > 0
            && Math.Abs(activeCarrier - publishedCarrier) <= 1_700;
    }

    public static int ParseChannelKhz(string? value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz)
            ? ToChannelKhz(mhz)
            : 0;
    }
}

public sealed class SiaRadioCatalog
{
    private readonly IReadOnlyDictionary<string, SiaAirportRadioRecord> airports;

    public SiaRadioCatalog(SiaRadioDataset dataset)
    {
        Dataset = dataset ?? throw new ArgumentNullException(nameof(dataset));
        Validate(dataset);
        airports = dataset.Airports
            .GroupBy(item => NormalizeIcao(item.Icao), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length == 4)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public SiaRadioDataset Dataset { get; }

    public static SiaRadioCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        var dataset = JsonSerializer.Deserialize<SiaRadioDataset>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            });
        return new SiaRadioCatalog(dataset ?? throw new InvalidDataException("Base radio SIA vide."));
    }

    public bool ContainsAirport(string? icao) => airports.ContainsKey(NormalizeIcao(icao));

    public SiaAirportRadioRecord? GetAirport(string? icao) =>
        airports.TryGetValue(NormalizeIcao(icao), out var airport) ? airport : null;

    public SiaRadioResolution Resolve(string? icao, double activeMhz, bool preferLocal)
    {
        var normalized = NormalizeIcao(icao);
        if (!airports.TryGetValue(normalized, out var airport))
        {
            return new SiaRadioResolution(false, false, false, null, null, Array.Empty<SiaRadioFrequencyRecord>(), "Aérodrome absent de la base SIA active.");
        }

        var matches = airport.Frequencies
            .Where(item => RadioChannel.Matches(item, activeMhz))
            .OrderByDescending(item => ScopePriority(item.Scope, preferLocal))
            .ThenByDescending(item => SafetyPriority(item))
            .ThenBy(item => item.ServiceCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length == 0)
        {
            return new SiaRadioResolution(true, false, false, airport, null, matches, "Canal actif absent des moyens radio SIA publiés pour cet aérodrome.");
        }

        var groupedOperationalModes = matches
            .Select(item => (item.Kind, item.Interactive, item.ScheduleState, item.Scope))
            .Distinct()
            .ToArray();
        var ambiguous = groupedOperationalModes.Length > 1
            && matches.Any(item => item.ScheduleState == SiaRadioScheduleState.PublishedNotEvaluated);

        SiaRadioFrequencyRecord selected;
        if (ambiguous)
        {
            selected = matches.FirstOrDefault(item => !item.Interactive)
                ?? matches[0];
        }
        else
        {
            selected = matches[0];
        }

        return new SiaRadioResolution(
            true,
            true,
            ambiguous,
            airport,
            selected,
            matches,
            ambiguous
                ? "Plusieurs modes publiés partagent ce canal et les horaires ne sont pas évalués : le mode silencieux est retenu par sécurité."
                : "Canal résolu depuis la base SIA active.");
    }

    public SiaRadioFrequencyRecord? Recommend(string? icao, bool isOnGround, bool dialogueOnly)
    {
        if (!airports.TryGetValue(NormalizeIcao(icao), out var airport))
        {
            return null;
        }

        return airport.Frequencies
            .Where(item => item.Scope == SiaRadioStationScope.Local)
            .Where(item => !dialogueOnly || item.Interactive)
            .OrderByDescending(item => RecommendationPriority(item, isOnGround))
            .ThenBy(item => item.ChannelKhz)
            .FirstOrDefault();
    }

    public static void Validate(SiaRadioDataset dataset)
    {
        if (dataset.SchemaVersion != 2)
        {
            throw new InvalidDataException($"Schéma radio SIA non pris en charge : {dataset.SchemaVersion}.");
        }

        if (!string.Equals(dataset.Authority, "SIA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La base radio ne déclare pas le SIA comme autorité source.");
        }

        if (dataset.EffectiveFrom == default || dataset.EffectiveUntil <= dataset.EffectiveFrom)
        {
            throw new InvalidDataException("Période AIRAC invalide.");
        }

        if (dataset.Airports.Count == 0)
        {
            throw new InvalidDataException("Aucun aérodrome dans la base radio SIA.");
        }

        var duplicates = dataset.Airports
            .GroupBy(item => NormalizeIcao(item.Icao), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Key.Length != 4 || group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicates.Length > 0)
        {
            throw new InvalidDataException($"ICAO invalides ou dupliqués : {string.Join(", ", duplicates)}.");
        }

        foreach (var airport in dataset.Airports)
        {
            foreach (var frequency in airport.Frequencies)
            {
                var channelKhz = frequency.ChannelKhz > 0
                    ? frequency.ChannelKhz
                    : RadioChannel.ParseChannelKhz(frequency.Channel);
                if (channelKhz is < 117_975 or > 137_000)
                {
                    throw new InvalidDataException($"Canal hors bande pour {airport.Icao} : {frequency.Channel}.");
                }

                frequency.ChannelKhz = channelKhz;
                frequency.Channel = RadioChannel.FormatChannel(channelKhz);
                frequency.CarrierHz = frequency.CarrierHz > 0
                    ? frequency.CarrierHz
                    : RadioChannel.CarrierHzFromChannelKhz(channelKhz);
                frequency.Callsign = string.IsNullOrWhiteSpace(frequency.Callsign)
                    ? $"{airport.Name} {frequency.ServiceCode}".Trim()
                    : frequency.Callsign.Trim();
            }
        }
    }

    private static int RecommendationPriority(SiaRadioFrequencyRecord item, bool isOnGround)
    {
        if (item.Interactive && item.ScheduleState == SiaRadioScheduleState.PublishedNotEvaluated)
        {
            return -100;
        }

        return item.Kind switch
        {
            SiaRadioServiceKind.Tower => 1000,
            SiaRadioServiceKind.Ground => isOnGround ? 960 : 650,
            SiaRadioServiceKind.Clearance => isOnGround ? 930 : 600,
            SiaRadioServiceKind.Approach => isOnGround ? 850 : 980,
            SiaRadioServiceKind.Departure => isOnGround ? 800 : 940,
            SiaRadioServiceKind.Information => 760,
            SiaRadioServiceKind.SelfInformation => 700,
            SiaRadioServiceKind.FlightInformation => isOnGround ? 500 : 900,
            SiaRadioServiceKind.ControlledOther => 680,
            SiaRadioServiceKind.AutomaticBroadcast => 200,
            SiaRadioServiceKind.RecordedMessage => 100,
            _ => 0,
        };
    }

    private static int ScopePriority(SiaRadioStationScope scope, bool preferLocal) =>
        (scope == SiaRadioStationScope.Local) == preferLocal ? 10 : 0;

    private static int SafetyPriority(SiaRadioFrequencyRecord item) =>
        item.Interactive ? 0 : 10;

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }
}

using System.Text.Json;
using Phonie.Models;

namespace Phonie.Services;

/// <summary>
/// Petit catalogue opérationnel vérifié servant de priorité aux fréquences de scène.
/// Les données sont chargées depuis data/radio/france-official.json lorsqu'il est présent.
/// Un secours embarqué LFBI/LFOU évite qu'une copie incomplète réactive les fréquences erronées de MSFS.
/// </summary>
public static class OfficialRadioCatalogService
{
    private const double FrequencyToleranceMhz = 0.0021;
    private static readonly Lazy<IReadOnlyDictionary<string, OfficialAirportDefinition>> Airports =
        new(LoadCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    public static OfficialRadioLookup Resolve(
        string? icao,
        double frequencyMhz,
        DateTimeOffset timestamp)
    {
        var normalized = NormalizeIcao(icao);
        if (!Airports.Value.TryGetValue(normalized, out var airport))
        {
            return new OfficialRadioLookup(false, null);
        }

        var entry = airport.Frequencies
            .Where(item => Matches(item.FrequencyMhz, frequencyMhz))
            .OrderBy(item => Math.Abs(item.FrequencyMhz - frequencyMhz))
            .FirstOrDefault();
        if (entry is null)
        {
            return new OfficialRadioLookup(true, null);
        }

        return new OfficialRadioLookup(
            true,
            BuildOperationalFrequency(airport, entry, timestamp));
    }

    public static OfficialRadioLookup Recommend(
        string? icao,
        bool isOnGround,
        DateTimeOffset timestamp)
    {
        var normalized = NormalizeIcao(icao);
        if (!Airports.Value.TryGetValue(normalized, out var airport))
        {
            return new OfficialRadioLookup(false, null);
        }

        var candidates = airport.Frequencies
            .Select(item => BuildOperationalFrequency(airport, item, timestamp))
            .Where(item => item.DialogueAllowed)
            .OrderByDescending(item => Priority(item.Kind, item.ServiceName, isOnGround))
            .ThenBy(item => item.FrequencyMhz)
            .ToArray();

        return new OfficialRadioLookup(true, candidates.FirstOrDefault());
    }

    public static IReadOnlyList<double> GetPublishedFrequencies(string? icao)
    {
        var normalized = NormalizeIcao(icao);
        return Airports.Value.TryGetValue(normalized, out var airport)
            ? airport.Frequencies
                .Select(item => item.FrequencyMhz)
                .Where(value => double.IsFinite(value) && value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToArray()
            : Array.Empty<double>();
    }

    private static OperationalFrequency BuildOperationalFrequency(
        OfficialAirportDefinition airport,
        OfficialFrequencyDefinition entry,
        DateTimeOffset timestamp)
    {
        var source = string.IsNullOrWhiteSpace(entry.Source)
            ? airport.Source
            : entry.Source;

        if (string.Equals(entry.ScheduleCode, "LFOU_AFIS", StringComparison.OrdinalIgnoreCase))
        {
            var afisOpen = IsLfouAfisOpen(timestamp);
            return afisOpen
                ? new OperationalFrequency(
                    entry.FrequencyMhz,
                    entry.ServiceName,
                    OperationalRadioKind.InformationService,
                    true,
                    "AFIS publié ouvert à cette heure : renseignements sans autorisation de contrôle.",
                    source)
                : new OperationalFrequency(
                    entry.FrequencyMhz,
                    "CHOLET A/A",
                    OperationalRadioKind.SelfInformation,
                    false,
                    "AFIS publié fermé à cette heure : fréquence utilisée en auto-information, PHONIE reste silencieux.",
                    source);
        }

        var kind = ParseKind(entry.Kind);
        return new OperationalFrequency(
            entry.FrequencyMhz,
            entry.ServiceName,
            kind,
            kind is OperationalRadioKind.Controlled or OperationalRadioKind.InformationService,
            Guidance(kind),
            source,
            entry.IsDuplicate);
    }

    private static int Priority(OperationalRadioKind kind, string serviceName, bool isOnGround)
    {
        var normalized = (serviceName ?? string.Empty).ToUpperInvariant();
        if (kind == OperationalRadioKind.Controlled)
        {
            if (normalized.Contains("TOUR", StringComparison.Ordinal)
                || normalized.Contains("TOWER", StringComparison.Ordinal))
            {
                return 1000;
            }

            if (normalized.Contains("SOL", StringComparison.Ordinal)
                || normalized.Contains("GROUND", StringComparison.Ordinal))
            {
                return isOnGround ? 950 : 600;
            }

            if (normalized.Contains("APPROCHE", StringComparison.Ordinal)
                || normalized.Contains("APPROACH", StringComparison.Ordinal))
            {
                return isOnGround ? 850 : 980;
            }

            return 800;
        }

        return kind == OperationalRadioKind.InformationService ? 750 : 0;
    }

    private static string Guidance(OperationalRadioKind kind) => kind switch
    {
        OperationalRadioKind.Controlled => "Organisme contrôlé publié : dialogue pilote-contrôleur autorisé.",
        OperationalRadioKind.InformationService => "Service d'information publié : renseignements sans autorisation de contrôle.",
        OperationalRadioKind.AutomaticBroadcast => "Diffusion automatique : PHONIE ne répond jamais au pilote.",
        OperationalRadioKind.RecordedMessage => "Message enregistré : aucun dialogue pilote-contrôleur.",
        OperationalRadioKind.SelfInformation => "Auto-information : PHONIE reste silencieux.",
        _ => "Service non classé : PHONIE reste silencieux.",
    };

    private static OperationalRadioKind ParseKind(string? value) =>
        Enum.TryParse<OperationalRadioKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : OperationalRadioKind.Unknown;

    private static IReadOnlyDictionary<string, OfficialAirportDefinition> LoadCatalog()
    {
        var fallback = BuildFallbackCatalog();
        var path = Path.Combine(AppPaths.DataDirectory, "radio", "france-official.json");
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            var document = JsonSerializer.Deserialize<OfficialRadioCatalogDocument>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (document?.Airports is null || document.Airports.Count == 0)
            {
                return fallback;
            }

            var loaded = document.Airports
                .Where(item => NormalizeIcao(item.Icao).Length == 4 && item.Frequencies.Count > 0)
                .ToDictionary(
                    item => NormalizeIcao(item.Icao),
                    item => item with { Icao = NormalizeIcao(item.Icao) },
                    StringComparer.OrdinalIgnoreCase);

            foreach (var item in fallback)
            {
                loaded.TryAdd(item.Key, item.Value);
            }

            return loaded;
        }
        catch
        {
            return fallback;
        }
    }

    private static IReadOnlyDictionary<string, OfficialAirportDefinition> BuildFallbackCatalog()
    {
        var airports = new[]
        {
            new OfficialAirportDefinition(
                "LFBI",
                "POITIERS BIARD",
                "SIA eAIP France - AD 2 LFBI / carte ARC, AIRAC 09 JUL 2026",
                new List<OfficialFrequencyDefinition>
                {
                    new(118.505, "POITIERS TOUR", nameof(OperationalRadioKind.Controlled), string.Empty,
                        "SIA eAIP France - AIRAC 09 JUL 2026", false),
                    new(134.100, "POITIERS APPROCHE / SIV", nameof(OperationalRadioKind.Controlled), string.Empty,
                        "SIA eAIP France - AIRAC 09 JUL 2026", false),
                    new(121.780, "POITIERS ATIS", nameof(OperationalRadioKind.AutomaticBroadcast), string.Empty,
                        "Profil opérationnel vérifié LFBI", false),
                    new(124.000, "RÉPONDEUR POITIERS", nameof(OperationalRadioKind.RecordedMessage), string.Empty,
                        "Profil opérationnel vérifié LFBI", false),
                }),
            new OfficialAirportDefinition(
                "LFOU",
                "CHOLET LE PONTREAU",
                "SIA eAIP France - AD 2 LFOU.18, valide 11 JUN 2026",
                new List<OfficialFrequencyDefinition>
                {
                    new(120.405, "CHOLET INFORMATION", nameof(OperationalRadioKind.InformationService), "LFOU_AFIS",
                        "SIA eAIP France - AD 2 LFOU.18, valide 11 JUN 2026", false),
                }),
        };

        return airports.ToDictionary(item => item.Icao, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLfouAfisOpen(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        var summer = IsEuropeanSummerTime(utc);
        var holidaySchedule = utc.DayOfWeek == DayOfWeek.Sunday || IsFrenchPublicHoliday(utc.Date);
        var minute = (utc.Hour * 60) + utc.Minute;

        // L'AIP France publie ces horaires en UTC hiver et demande de retirer
        // une heure aux horaires été. Les bornes été ci-dessous sont donc déjà
        // converties en UTC : 0430-0900 / 1030-1600 et 1100-1600 DIM/JF.
        if (summer)
        {
            return holidaySchedule
                ? minute is >= 660 and < 960
                : minute is >= 270 and < 540 or >= 630 and < 960;
        }

        return holidaySchedule
            ? minute is >= 780 and < 1080
            : minute is >= 420 and < 660 or >= 750 and < 1080;
    }

    private static bool IsEuropeanSummerTime(DateTimeOffset utc)
    {
        var year = utc.Year;
        var start = LastSunday(year, 3).AddHours(1);
        var end = LastSunday(year, 10).AddHours(1);
        return utc >= start && utc < end;
    }

    private static DateTimeOffset LastSunday(int year, int month)
    {
        var day = DateTime.DaysInMonth(year, month);
        var date = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        while (date.DayOfWeek != DayOfWeek.Sunday)
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private static bool IsFrenchPublicHoliday(DateTime date)
    {
        var easter = EasterSunday(date.Year);
        var holidays = new HashSet<DateTime>
        {
            new(date.Year, 1, 1),
            easter.AddDays(1),
            new(date.Year, 5, 1),
            new(date.Year, 5, 8),
            easter.AddDays(39),
            easter.AddDays(50),
            new(date.Year, 7, 14),
            new(date.Year, 8, 15),
            new(date.Year, 11, 1),
            new(date.Year, 11, 11),
            new(date.Year, 12, 25),
        };
        return holidays.Contains(date.Date);
    }

    private static DateTime EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateTime(year, month, day);
    }

    private static bool Matches(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= FrequencyToleranceMhz;

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }

    public sealed record OfficialRadioLookup(bool AirportKnown, OperationalFrequency? Frequency);

    private sealed record OfficialRadioCatalogDocument(
        int SchemaVersion,
        string Dataset,
        string ValidFrom,
        List<OfficialAirportDefinition> Airports);

    private sealed record OfficialAirportDefinition(
        string Icao,
        string Name,
        string Source,
        List<OfficialFrequencyDefinition> Frequencies);

    private sealed record OfficialFrequencyDefinition(
        double FrequencyMhz,
        string ServiceName,
        string Kind,
        string ScheduleCode,
        string Source,
        bool IsDuplicate);
}

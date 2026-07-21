using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonie.Core;
using Phonie.Models;

namespace Phonie.Services;

/// <summary>
/// Catalogue radio français dérivé exclusivement des publications officielles du SIA.
/// Aucune fréquence d'aérodrome n'est codée dans l'application. Le seul secours est
/// une précédente base SIA locale dont l'intégrité a été validée.
/// </summary>
public static class OfficialRadioCatalogService
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static SiaRadioCatalog? catalog;
    private static SiaRadioManifest? manifest;
    private static SiaRadioDatabaseStatus status = Unavailable("Base radio SIA non chargée.");

    public static event EventHandler<SiaRadioDatabaseStatus>? StatusChanged;

    public static SiaRadioDatabaseStatus Status
    {
        get
        {
            lock (Gate)
            {
                return status;
            }
        }
    }

    public static SiaRadioDatabaseStatus Reload(DateTimeOffset? timestamp = null)
    {
        lock (Gate)
        {
            var now = timestamp ?? DateTimeOffset.UtcNow;
            Directory.CreateDirectory(AppPaths.FranceRadioDataDirectory);
            var manifestPath = AppPaths.FranceRadioManifestPath;
            if (!File.Exists(manifestPath))
            {
                catalog = null;
                manifest = null;
                return PublishStatus(Unavailable("Manifest radio SIA absent. Utiliser la mise à jour des données France."));
            }

            try
            {
                manifest = JsonSerializer.Deserialize<SiaRadioManifest>(File.ReadAllText(manifestPath), JsonOptions)
                    ?? throw new InvalidDataException("Manifest radio SIA vide.");
                ValidateManifest(manifest);

                if (manifest.BootstrapRequired)
                {
                    catalog = null;
                    return PublishStatus(Unavailable("Base radio SIA à générer par le workflow officiel."));
                }

                ActivateDueNext(manifest, now, manifestPath);
                var candidates = BuildCandidateList(manifest, now);
                var errors = new List<string>();
                foreach (var candidate in candidates)
                {
                    try
                    {
                        var loaded = LoadDescriptor(candidate.Descriptor);
                        catalog = loaded;
                        var dataset = loaded.Dataset;
                        var stale = now >= dataset.EffectiveUntil;
                        var next = manifest.Next;
                        var nextCycle = next is not null && next.EffectiveFrom > now ? next.AiracCycle : null;
                        var nextDate = next is not null && next.EffectiveFrom > now ? next.EffectiveFrom : null;
                        var message = stale
                            ? "Dernière base SIA validée utilisée hors période AIRAC : vérifier la mise à jour."
                            : candidate.Label == "current"
                                ? "Base SIA active et intègre."
                                : $"Base SIA de secours utilisée ({candidate.Label}).";
                        return PublishStatus(new SiaRadioDatabaseStatus(
                            true,
                            true,
                            stale ? "STALE" : candidate.Label.ToUpperInvariant(),
                            dataset.Revision,
                            dataset.AiracCycle,
                            dataset.EffectiveFrom,
                            dataset.EffectiveUntil,
                            dataset.GeneratedAt,
                            dataset.Airports.Count,
                            dataset.Airports.Sum(item => item.Frequencies.Count),
                            ResolvePath(candidate.Descriptor.RelativePath),
                            $"SIA - {dataset.SourceKind}",
                            message,
                            nextCycle,
                            nextDate));
                    }
                    catch (Exception exception)
                    {
                        errors.Add($"{candidate.Label}: {Clean(exception)}");
                    }
                }

                catalog = null;
                return PublishStatus(Unavailable(
                    errors.Count == 0
                        ? "Aucun jeu de données SIA valide dans le manifest."
                        : $"Jeux de données SIA rejetés : {string.Join(" | ", errors)}"));
            }
            catch (Exception exception)
            {
                catalog = null;
                manifest = null;
                return PublishStatus(Unavailable($"Chargement de la base radio SIA impossible : {Clean(exception)}"));
            }
        }
    }

    public static OfficialRadioLookup Resolve(
        string? icao,
        double frequencyMhz,
        DateTimeOffset timestamp)
    {
        EnsureLoaded(timestamp);
        lock (Gate)
        {
            var normalized = NormalizeIcao(icao);
            if (catalog is null)
            {
                return new OfficialRadioLookup(false, false, null, status.Message);
            }

            var resolution = catalog.Resolve(normalized, frequencyMhz, preferLocal: true);
            if (!resolution.FrequencyKnown || resolution.Frequency is null)
            {
                return new OfficialRadioLookup(true, resolution.AirportKnown, null, resolution.Reason);
            }

            return new OfficialRadioLookup(
                true,
                resolution.AirportKnown,
                BuildOperationalFrequency(resolution, catalog.Dataset),
                resolution.Reason);
        }
    }

    public static OfficialRadioLookup Recommend(
        string? icao,
        bool isOnGround,
        DateTimeOffset timestamp,
        bool dialogueOnly = false)
    {
        EnsureLoaded(timestamp);
        lock (Gate)
        {
            if (catalog is null)
            {
                return new OfficialRadioLookup(false, false, null, status.Message);
            }

            var normalized = NormalizeIcao(icao);
            var airportKnown = catalog.ContainsAirport(normalized);
            var frequency = catalog.Recommend(normalized, isOnGround, dialogueOnly);
            if (frequency is null)
            {
                return new OfficialRadioLookup(true, airportKnown, null, "Aucune fréquence locale officielle répondant au filtre.");
            }

            var airport = catalog.GetAirport(normalized)!;
            var resolution = new SiaRadioResolution(
                true,
                true,
                false,
                airport,
                frequency,
                new[] { frequency },
                "Fréquence locale recommandée depuis la base SIA active.");
            return new OfficialRadioLookup(
                true,
                true,
                BuildOperationalFrequency(resolution, catalog.Dataset),
                resolution.Reason);
        }
    }

    public static IReadOnlyList<double> GetPublishedFrequencies(string? icao)
    {
        EnsureLoaded(DateTimeOffset.UtcNow);
        lock (Gate)
        {
            var airport = catalog?.GetAirport(icao);
            return airport is null
                ? Array.Empty<double>()
                : airport.Frequencies
                    .Select(item => item.ChannelKhz / 1000d)
                    .Distinct()
                    .OrderBy(value => value)
                    .ToArray();
        }
    }

    public static IReadOnlyList<OperationalFrequency> GetPublishedServices(string? icao)
    {
        EnsureLoaded(DateTimeOffset.UtcNow);
        lock (Gate)
        {
            var airport = catalog?.GetAirport(icao);
            if (airport is null || catalog is null)
            {
                return Array.Empty<OperationalFrequency>();
            }

            return airport.Frequencies
                .Select(record => BuildOperationalFrequency(
                    new SiaRadioResolution(
                        true,
                        true,
                        false,
                        airport,
                        record,
                        new[] { record },
                        "Service publié dans la base SIA active."),
                    catalog.Dataset))
                .OrderBy(item => item.FrequencyMhz)
                .ThenBy(item => item.ServiceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public static bool IsFrenchIcao(string? icao)
    {
        var normalized = NormalizeIcao(icao);
        return normalized.StartsWith("LF", StringComparison.Ordinal)
            || normalized.StartsWith("TF", StringComparison.Ordinal)
            || normalized.StartsWith("FM", StringComparison.Ordinal)
            || normalized.StartsWith("NT", StringComparison.Ordinal)
            || normalized.StartsWith("NW", StringComparison.Ordinal);
    }

    private static OperationalFrequency BuildOperationalFrequency(
        SiaRadioResolution resolution,
        SiaRadioDataset dataset)
    {
        var record = resolution.Frequency!;
        var airport = resolution.Airport!;
        var kind = MapKind(record.Kind);
        var dialogueAllowed = record.Interactive
            && record.ScheduleState != SiaRadioScheduleState.PublishedNotEvaluated
            && !resolution.Ambiguous
            && kind is OperationalRadioKind.Controlled or OperationalRadioKind.InformationService;
        var serviceName = string.IsNullOrWhiteSpace(record.Callsign)
            ? $"{airport.Name} {record.ServiceCode}".Trim()
            : record.Callsign.Trim();
        var sourceReference = string.IsNullOrWhiteSpace(record.SourceReference)
            ? airport.SourceReference
            : record.SourceReference;
        var source = $"SIA {dataset.AiracCycle} - {sourceReference}".Trim(' ', '-');
        var guidance = resolution.Ambiguous
            ? resolution.Reason
            : Guidance(kind, record.ScheduleState, record.HoursText, record.Scope);
        var stationKey = string.Join(
            "|",
            record.Scope == SiaRadioStationScope.Local ? airport.Icao : "REGIONAL",
            record.Callsign.Trim().ToUpperInvariant(),
            record.ServiceCode.Trim().ToUpperInvariant());

        return new OperationalFrequency(
            record.ChannelKhz / 1000d,
            serviceName,
            kind,
            dialogueAllowed,
            guidance,
            source,
            false,
            stationKey,
            record.Scope.ToString(),
            dataset.Revision,
            record.Channel);
    }

    private static OperationalRadioKind MapKind(SiaRadioServiceKind kind) => kind switch
    {
        SiaRadioServiceKind.Tower or
        SiaRadioServiceKind.Ground or
        SiaRadioServiceKind.Clearance or
        SiaRadioServiceKind.Approach or
        SiaRadioServiceKind.Departure or
        SiaRadioServiceKind.ControlledOther => OperationalRadioKind.Controlled,
        SiaRadioServiceKind.Information or
        SiaRadioServiceKind.FlightInformation => OperationalRadioKind.InformationService,
        SiaRadioServiceKind.SelfInformation => OperationalRadioKind.SelfInformation,
        SiaRadioServiceKind.AutomaticBroadcast => OperationalRadioKind.AutomaticBroadcast,
        SiaRadioServiceKind.RecordedMessage => OperationalRadioKind.RecordedMessage,
        _ => OperationalRadioKind.Unknown,
    };

    private static string Guidance(
        OperationalRadioKind kind,
        SiaRadioScheduleState schedule,
        string hoursText,
        SiaRadioStationScope scope)
    {
        var scopeText = scope == SiaRadioStationScope.Regional ? "Service régional. " : string.Empty;
        var scheduleText = schedule == SiaRadioScheduleState.PublishedNotEvaluated
            ? string.IsNullOrWhiteSpace(hoursText)
                ? "Horaires publiés non évalués automatiquement. "
                : $"Horaires SIA non évalués automatiquement : {hoursText}. "
            : string.Empty;
        return scopeText + scheduleText + (kind switch
        {
            OperationalRadioKind.Controlled => "Organisme contrôlé publié par le SIA : dialogue autorisé lorsque le service est actif.",
            OperationalRadioKind.InformationService => "Service d'information publié par le SIA : renseignements sans autorisation de contrôle.",
            OperationalRadioKind.AutomaticBroadcast => "Diffusion automatique publiée par le SIA : PHONIE ne répond jamais au pilote.",
            OperationalRadioKind.RecordedMessage => "Message enregistré publié par le SIA : aucun dialogue pilote-contrôleur.",
            OperationalRadioKind.SelfInformation => "Auto-information publiée par le SIA : PHONIE reste silencieux.",
            _ => "Service SIA non classé : PHONIE reste silencieux.",
        });
    }

    private static void EnsureLoaded(DateTimeOffset timestamp)
    {
        lock (Gate)
        {
            if (catalog is null)
            {
                Reload(timestamp);
            }
        }
    }

    private static SiaRadioCatalog LoadDescriptor(SiaRadioDatasetDescriptor descriptor)
    {
        if (descriptor.AirportCount < 100 || descriptor.FrequencyCount < 100)
        {
            throw new InvalidDataException(
                $"Couverture nationale insuffisante ({descriptor.AirportCount} aérodromes, {descriptor.FrequencyCount} fréquences).");
        }

        var path = ResolvePath(descriptor.RelativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Jeu de données absent.", path);
        }

        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(sha, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SHA-256 du jeu de données incorrect.");
        }

        var loaded = SiaRadioCatalog.Load(path);
        if (!string.Equals(loaded.Dataset.Revision, descriptor.Revision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Révision du jeu de données différente du manifest.");
        }

        if (loaded.Dataset.Airports.Count != descriptor.AirportCount)
        {
            throw new InvalidDataException("Nombre d'aérodromes différent du manifest.");
        }

        return loaded;
    }

    private static IReadOnlyList<(string Label, SiaRadioDatasetDescriptor Descriptor)> BuildCandidateList(
        SiaRadioManifest value,
        DateTimeOffset now)
    {
        var result = new List<(string, SiaRadioDatasetDescriptor)>();
        if (value.Current is not null)
        {
            result.Add(("current", value.Current));
        }

        if (value.Previous is not null)
        {
            result.Add(("previous", value.Previous));
        }

        if (value.Next is not null && value.Next.EffectiveFrom <= now)
        {
            result.Insert(0, ("next-due", value.Next));
        }

        return result;
    }

    private static void ActivateDueNext(SiaRadioManifest value, DateTimeOffset now, string manifestPath)
    {
        if (value.Next is null || value.Next.EffectiveFrom > now)
        {
            return;
        }

        _ = LoadDescriptor(value.Next);
        value.Previous = value.Current;
        value.Current = value.Next;
        value.Next = null;
        value.DatasetRevision = value.Current.Revision;
        WriteManifestAtomically(manifestPath, value);
    }

    private static void WriteManifestAtomically(string path, SiaRadioManifest value)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporary, path, true);
    }

    private static void ValidateManifest(SiaRadioManifest value)
    {
        if (value.SchemaVersion != 2)
        {
            throw new InvalidDataException($"Schéma manifest non pris en charge : {value.SchemaVersion}.");
        }

        if (!string.Equals(value.Authority, "SIA", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Autorité du manifest différente du SIA.");
        }
    }

    private static string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Chemin de jeu de données radio invalide.");
        }

        return Path.GetFullPath(Path.Combine(AppPaths.FranceRadioDataDirectory, relativePath));
    }

    private static SiaRadioDatabaseStatus PublishStatus(SiaRadioDatabaseStatus value)
    {
        status = value;
        StatusChanged?.Invoke(null, value);
        return value;
    }

    private static SiaRadioDatabaseStatus Unavailable(string message) => new(
        false,
        false,
        "UNAVAILABLE",
        string.Empty,
        string.Empty,
        default,
        default,
        default,
        0,
        0,
        string.Empty,
        "SIA",
        message);

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }

    private static string Clean(Exception exception) =>
        exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();

    public sealed record OfficialRadioLookup(
        bool DatabaseAvailable,
        bool AirportKnown,
        OperationalFrequency? Frequency,
        string Reason);
}

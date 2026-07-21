using Phonie.Core;
using Phonie.Models;

namespace Phonie.Services;

public static class OperationalRadioService
{
    private const double LegacyFacilityToleranceMhz = 0.0021;

    public static OperationalFrequency Resolve(
        SimulatorSnapshot snapshot,
        AirportFacilityReport? airportReport,
        string? radioAirportIcao = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var frequency = snapshot.Com1ActiveMhz;
        var resolvedIcao = NormalizeIcao(
            string.IsNullOrWhiteSpace(radioAirportIcao)
                ? snapshot.RadioAirportIcao
                : radioAirportIcao);
        var official = OfficialRadioCatalogService.Resolve(
            resolvedIcao,
            frequency,
            snapshot.Timestamp);
        if (official.Frequency is not null)
        {
            return official.Frequency;
        }

        // Pour les aérodromes français, la base SIA est l'unique autorité radio.
        // Une fréquence Facilities ou un type COM SimConnect ne peut jamais la remplacer.
        if (OfficialRadioCatalogService.IsFrenchIcao(resolvedIcao))
        {
            return Unknown(
                frequency,
                official.DatabaseAvailable
                    ? official.Reason
                    : "Base radio SIA indisponible : aucune fréquence française de secours n'est inventée.",
                official.DatabaseAvailable ? "Base SIA active" : "Données officielles indisponibles");
        }

        // Hors périmètre français, les Facilities restent un secours diagnostique générique.
        var facilityFrequency = airportReport?.Frequencies
            .Where(item => MatchesLegacy(item.FrequencyMhz, frequency))
            .OrderBy(item => Math.Abs(item.FrequencyMhz - frequency))
            .FirstOrDefault();
        var facilityResolution = facilityFrequency is not null && airportReport is not null
            ? ResolveFacilityFrequency(facilityFrequency, airportReport, resolvedIcao)
            : null;

        if (facilityResolution?.Kind is OperationalRadioKind.SelfInformation
            or OperationalRadioKind.AutomaticBroadcast
            or OperationalRadioKind.RecordedMessage)
        {
            return facilityResolution;
        }

        var policy = RadioPolicyResolver.Resolve(snapshot.Com1StationType);
        if (policy.Kind != RadioPolicyKind.Unknown)
        {
            return policy.Kind switch
            {
                RadioPolicyKind.Controlled => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.Controlled,
                    true,
                    policy.Guidance,
                    "Type COM SimConnect - hors France"),
                RadioPolicyKind.InformationService => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.InformationService,
                    true,
                    policy.Guidance,
                    "Type COM SimConnect - hors France"),
                RadioPolicyKind.AutomaticInformation => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.AutomaticBroadcast,
                    false,
                    policy.Guidance,
                    "Type COM SimConnect - hors France"),
                RadioPolicyKind.SelfInformation => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.SelfInformation,
                    false,
                    policy.Guidance,
                    "Type COM SimConnect - hors France"),
                _ => Unknown(frequency),
            };
        }

        return facilityResolution ?? Unknown(frequency);
    }

    public static OperationalFrequency? Recommend(
        AirportFacilityReport? airportReport,
        string? resolvedIcao,
        bool isOnGround,
        DateTimeOffset? timestamp = null)
    {
        var reportIcao = airportReport is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(airportReport.Icao)
                ? airportReport.RequestedIcao
                : airportReport.Icao;
        var normalizedIcao = NormalizeIcao(
            string.IsNullOrWhiteSpace(resolvedIcao) ? reportIcao : resolvedIcao);
        var official = OfficialRadioCatalogService.Recommend(
            normalizedIcao,
            isOnGround,
            timestamp ?? DateTimeOffset.UtcNow,
            dialogueOnly: false);
        if (OfficialRadioCatalogService.IsFrenchIcao(normalizedIcao))
        {
            return official.Frequency;
        }

        if (official.Frequency is not null)
        {
            return official.Frequency;
        }

        if (airportReport is null || airportReport.Frequencies.Count == 0)
        {
            return null;
        }

        var candidates = airportReport.Frequencies
            .Select(item => new AirportRadioCandidate(item.Type, item.FrequencyMhz, item.Name))
            .ToArray();
        var recommendation = AirportRadioSelector.Recommend(candidates, isOnGround);
        if (recommendation is null)
        {
            return null;
        }

        var selected = airportReport.Frequencies
            .OrderBy(item => Math.Abs(item.FrequencyMhz - recommendation.FrequencyMhz))
            .FirstOrDefault();
        if (selected is null)
        {
            return null;
        }

        var resolved = ResolveFacilityFrequency(selected, airportReport, normalizedIcao);
        return resolved.DialogueAllowed ? resolved : null;
    }

    private static OperationalFrequency ResolveFacilityFrequency(
        AirportFrequencyData facilityFrequency,
        AirportFacilityReport report,
        string resolvedIcao)
    {
        var serviceName = BuildFacilityServiceName(facilityFrequency, report, resolvedIcao);
        const string source = "Fréquence Facilities - hors périmètre France";
        return facilityFrequency.Type switch
        {
            1 => new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.AutomaticBroadcast, false,
                "Information automatique : diffusion sans dialogue.", source),
            2 or 3 or 4 => new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.SelfInformation, false,
                "Auto-information : PHONIE reste silencieux.", source),
            5 or 6 or 7 or 8 or 9 or 10 or 14 or 15 => new OperationalFrequency(
                facilityFrequency.FrequencyMhz,
                serviceName,
                OperationalRadioKind.Controlled,
                true,
                "Organisme contrôlé identifié dans les Facilities.",
                source),
            11 => new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.InformationService, true,
                "Service d'information / AFIS : renseignements sans autorisation de contrôle.", source),
            12 or 13 => new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.AutomaticBroadcast, false,
                "Information météo automatique : diffusion sans dialogue.", source),
            _ => InferFromFacilityName(facilityFrequency, serviceName, source),
        };
    }

    private static OperationalFrequency InferFromFacilityName(
        AirportFrequencyData facilityFrequency,
        string serviceName,
        string source)
    {
        var name = facilityFrequency.Name?.Trim().ToUpperInvariant() ?? string.Empty;
        if (ContainsAny(name, "CTAF", "UNICOM", "MULTICOM", "AUTO-INFO", "AUTO INFO", "A/A", "A-A", "ADVISORY"))
        {
            return new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.SelfInformation, false,
                "Auto-information identifiée par son libellé Facilities.", source);
        }

        if (ContainsAny(name, "AFIS", "INFORMATION", "FSS")
            || string.Equals(name, "INFO", StringComparison.OrdinalIgnoreCase))
        {
            return new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.InformationService, true,
                "Service d'information identifié par son libellé Facilities.", source);
        }

        if (ContainsAny(name, "ATIS", "AWOS", "ASOS", "AWS"))
        {
            return new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.AutomaticBroadcast, false,
                "Information automatique identifiée par son libellé Facilities.", source);
        }

        if (ContainsAny(name, "TOWER", "TOUR", "GROUND", "SOL", "APPROACH", "APPROCHE", "DEPARTURE", "DÉPART"))
        {
            return new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.Controlled, true,
                "Organisme contrôlé identifié par son libellé Facilities.", source);
        }

        return Unknown(facilityFrequency.FrequencyMhz);
    }

    private static OperationalFrequency Unknown(
        double frequency,
        string guidance = "PHONIE reste silencieux tant que le service n'est pas déterminé.",
        string source = "Aucune classification") => new(
        frequency,
        "FRÉQUENCE NON IDENTIFIÉE",
        OperationalRadioKind.Unknown,
        false,
        guidance,
        source);

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string BuildFacilityServiceName(
        AirportFrequencyData frequency,
        AirportFacilityReport report,
        string resolvedIcao)
    {
        var name = frequency.Name?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var icao = NormalizeIcao(string.IsNullOrWhiteSpace(resolvedIcao)
            ? (string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao)
            : resolvedIcao);
        return string.IsNullOrWhiteSpace(icao) ? "STATION" : icao;
    }

    private static bool MatchesLegacy(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= LegacyFacilityToleranceMhz;

    private static string FriendlyService(SimulatorSnapshot snapshot, string resolvedIcao)
    {
        var ident = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent)
            ? (string.IsNullOrWhiteSpace(resolvedIcao) ? "STATION" : resolvedIcao)
            : snapshot.Com1StationIdent.Trim().ToUpperInvariant();
        var type = snapshot.Com1StationType?.Trim().ToUpperInvariant();
        var service = string.IsNullOrWhiteSpace(type) ? ident : $"{ident} {type}";
        return string.IsNullOrWhiteSpace(resolvedIcao)
            || service.Contains(resolvedIcao, StringComparison.OrdinalIgnoreCase)
            ? service
            : $"{resolvedIcao} - {service}";
    }

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }
}

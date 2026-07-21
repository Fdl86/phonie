using Phonie.Models;

namespace Phonie.Services;

public static class OperationalRadioService
{
    private const double FrequencyToleranceMhz = 0.0021;

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
        var isLfbi = string.Equals(resolvedIcao, "LFBI", StringComparison.OrdinalIgnoreCase);

        if (isLfbi)
        {
            if (Matches(frequency, 121.780))
            {
                return new OperationalFrequency(frequency, "POITIERS ATIS", OperationalRadioKind.AutomaticBroadcast, false,
                    "Diffusion automatique. PHONIE ne répond jamais au pilote.", "Profil opérationnel LFBI");
            }

            if (Matches(frequency, 124.000))
            {
                return new OperationalFrequency(frequency, "RÉPONDEUR POITIERS", OperationalRadioKind.RecordedMessage, false,
                    "Message enregistré réel. Aucun dialogue pilote-contrôleur.", "Profil opérationnel LFBI");
            }

            if (Matches(frequency, 134.100))
            {
                return new OperationalFrequency(frequency, "POITIERS APPROCHE / SIV", OperationalRadioKind.InformationService, true,
                    "Service d'information et de contrôle. Dialogue autorisé.", "Profil opérationnel LFBI");
            }

            if (Matches(frequency, 118.505))
            {
                return new OperationalFrequency(frequency, "POITIERS TOUR", OperationalRadioKind.Controlled, true,
                    "Organisme contrôlé. Dialogue pilote-contrôleur autorisé.", "Fréquence active MSFS 2024");
            }

            if (Matches(frequency, 118.500))
            {
                var duplicate = airportReport?.Frequencies.Any(item => Matches(item.FrequencyMhz, 118.505)) == true;
                var source = duplicate ? "Entrée héritée ou doublon de scène" : "Fréquence active MSFS 2020";
                return new OperationalFrequency(frequency, "POITIERS TOUR", OperationalRadioKind.Controlled, true,
                    duplicate
                        ? "Tour détectée, avec doublon 118.505 dans la scène ou la base du simulateur."
                        : "Organisme contrôlé. Dialogue pilote-contrôleur autorisé.",
                    source,
                    duplicate);
            }
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
                    "Type COM SimConnect"),
                RadioPolicyKind.InformationService => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.InformationService,
                    true,
                    policy.Guidance,
                    "Type COM SimConnect"),
                RadioPolicyKind.AutomaticInformation => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.AutomaticBroadcast,
                    false,
                    policy.Guidance,
                    "Type COM SimConnect"),
                RadioPolicyKind.SelfInformation => new OperationalFrequency(
                    frequency,
                    FriendlyService(snapshot, resolvedIcao),
                    OperationalRadioKind.SelfInformation,
                    false,
                    policy.Guidance,
                    "Type COM SimConnect"),
                _ => Unknown(frequency),
            };
        }

        var facilityFrequency = airportReport?.Frequencies
            .Where(item => Matches(item.FrequencyMhz, frequency))
            .OrderBy(item => Math.Abs(item.FrequencyMhz - frequency))
            .FirstOrDefault();
        if (facilityFrequency is not null)
        {
            return ResolveFacilityFrequency(facilityFrequency, airportReport, resolvedIcao);
        }

        return Unknown(frequency);
    }

    private static OperationalFrequency ResolveFacilityFrequency(
        AirportFrequencyData facilityFrequency,
        AirportFacilityReport report,
        string resolvedIcao)
    {
        var serviceName = BuildFacilityServiceName(facilityFrequency, report, resolvedIcao);
        const string source = "Fréquence Facilities du contexte radio";
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
        if (ContainsAny(name, "AFIS", "INFO", "INFORMATION", "FSS"))
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

        if (ContainsAny(name, "CTAF", "UNICOM", "MULTICOM", "AUTO-INFO", "AUTO INFO"))
        {
            return new OperationalFrequency(facilityFrequency.FrequencyMhz, serviceName, OperationalRadioKind.SelfInformation, false,
                "Auto-information identifiée par son libellé Facilities.", source);
        }

        return Unknown(facilityFrequency.FrequencyMhz);
    }

    private static OperationalFrequency Unknown(double frequency) => new(
        frequency,
        "FRÉQUENCE NON IDENTIFIÉE",
        OperationalRadioKind.Unknown,
        false,
        "PHONIE reste silencieux tant que le service n'est pas déterminé.",
        "Aucune classification");

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

    private static bool Matches(double left, double right) =>
        double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= FrequencyToleranceMhz;

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

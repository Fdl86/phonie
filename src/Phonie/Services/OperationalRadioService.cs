using Phonie.Models;

namespace Phonie.Services;

public static class OperationalRadioService
{
    private const double FrequencyToleranceMhz = 0.0021;

    public static OperationalFrequency Resolve(SimulatorSnapshot snapshot, AirportFacilityReport? airportReport)
    {
        var frequency = snapshot.Com1ActiveMhz;
        var isLfbi = string.Equals(snapshot.Com1StationIdent, "LFBI", StringComparison.OrdinalIgnoreCase)
            || snapshot.DistanceToLfbiNm <= 25;

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
        return policy.Kind switch
        {
            RadioPolicyKind.Controlled => new OperationalFrequency(frequency, FriendlyService(snapshot), OperationalRadioKind.Controlled, true, policy.Guidance, "Type COM SimConnect"),
            RadioPolicyKind.InformationService => new OperationalFrequency(frequency, FriendlyService(snapshot), OperationalRadioKind.InformationService, true, policy.Guidance, "Type COM SimConnect"),
            RadioPolicyKind.AutomaticInformation => new OperationalFrequency(frequency, FriendlyService(snapshot), OperationalRadioKind.AutomaticBroadcast, false, policy.Guidance, "Type COM SimConnect"),
            RadioPolicyKind.SelfInformation => new OperationalFrequency(frequency, FriendlyService(snapshot), OperationalRadioKind.SelfInformation, false, policy.Guidance, "Type COM SimConnect"),
            _ => new OperationalFrequency(frequency, "FRÉQUENCE NON IDENTIFIÉE", OperationalRadioKind.Unknown, false,
                "PHONIE reste silencieux tant que le service n'est pas déterminé.", "Aucune classification"),
        };
    }

    private static bool Matches(double left, double right) => Math.Abs(left - right) <= FrequencyToleranceMhz;

    private static string FriendlyService(SimulatorSnapshot snapshot)
    {
        var ident = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent) ? "STATION" : snapshot.Com1StationIdent.Trim().ToUpperInvariant();
        var type = snapshot.Com1StationType?.Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(type) ? ident : $"{ident} {type}";
    }
}

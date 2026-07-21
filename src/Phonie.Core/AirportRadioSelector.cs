namespace Phonie.Core;

public enum AirportRadioServiceKind
{
    Unknown,
    Tower,
    Ground,
    Clearance,
    Approach,
    Departure,
    Information,
    ControlledOther,
    SelfInformation,
    AutomaticBroadcast,
}

public sealed record AirportRadioCandidate(
    int Type,
    double FrequencyMhz,
    string Name);

public sealed record AirportRadioRecommendation(
    double FrequencyMhz,
    string Name,
    AirportRadioServiceKind Kind,
    int Priority,
    string Reason);

public static class AirportRadioSelector
{
    public static AirportRadioRecommendation? Recommend(
        IReadOnlyList<AirportRadioCandidate>? frequencies,
        bool isOnGround)
    {
        if (frequencies is null || frequencies.Count == 0)
        {
            return null;
        }

        return frequencies
            .Where(item => double.IsFinite(item.FrequencyMhz) && item.FrequencyMhz > 0)
            .Select(item =>
            {
                var kind = Classify(item);
                var priority = Priority(kind, isOnGround);
                return new AirportRadioRecommendation(
                    item.FrequencyMhz,
                    NormalizeName(item.Name),
                    kind,
                    priority,
                    BuildReason(kind));
            })
            .Where(item => item.Priority > 0)
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.FrequencyMhz)
            .FirstOrDefault();
    }

    public static AirportRadioServiceKind Classify(AirportRadioCandidate frequency)
    {
        var name = NormalizeName(frequency.Name);

        if (ContainsAny(name, "CTAF", "UNICOM", "MULTICOM", "AUTO-INFO", "AUTO INFO", "A/A", "A-A", "ADVISORY"))
        {
            return AirportRadioServiceKind.SelfInformation;
        }

        if (ContainsAny(name, "ATIS", "AWOS", "ASOS", "AWS", "MÉTÉO AUTO", "METEO AUTO"))
        {
            return AirportRadioServiceKind.AutomaticBroadcast;
        }

        if (ContainsAny(name, "TOWER", "TOUR", " TWR", "TWR ") || frequency.Type == 6)
        {
            return AirportRadioServiceKind.Tower;
        }

        if (ContainsAny(name, "GROUND", "SOL", " GND", "GND ") || frequency.Type == 5)
        {
            return AirportRadioServiceKind.Ground;
        }

        if (ContainsAny(name, "CLEARANCE", "DELIVERY", "PRÉVOL", "PREVOL", " CLR", "CLR ") || frequency.Type == 7)
        {
            return AirportRadioServiceKind.Clearance;
        }

        if (ContainsAny(name, "APPROACH", "APPROCHE", " APPR", "APPR ", " APP", "APP ") || frequency.Type == 8)
        {
            return AirportRadioServiceKind.Approach;
        }

        if (ContainsAny(name, "DEPARTURE", "DÉPART", "DEPART", " DEP", "DEP ") || frequency.Type == 9)
        {
            return AirportRadioServiceKind.Departure;
        }

        if (ContainsAny(name, "AFIS", "FSS", "INFORMATION", " INFO", "INFO ") || frequency.Type == 11)
        {
            return AirportRadioServiceKind.Information;
        }

        if (frequency.Type is 2 or 3 or 4)
        {
            return AirportRadioServiceKind.SelfInformation;
        }

        if (frequency.Type is 1 or 12 or 13)
        {
            return AirportRadioServiceKind.AutomaticBroadcast;
        }

        if (frequency.Type is 10 or 14 or 15)
        {
            return AirportRadioServiceKind.ControlledOther;
        }

        return AirportRadioServiceKind.Unknown;
    }

    public static bool IsSilent(AirportRadioServiceKind kind) =>
        kind is AirportRadioServiceKind.SelfInformation
            or AirportRadioServiceKind.AutomaticBroadcast
            or AirportRadioServiceKind.Unknown;

    private static int Priority(AirportRadioServiceKind kind, bool isOnGround) => kind switch
    {
        // Au sol et aux abords immédiats, la Tour est la fréquence de dialogue générique préférée.
        AirportRadioServiceKind.Tower => 1000,
        AirportRadioServiceKind.Ground => isOnGround ? 950 : 650,
        AirportRadioServiceKind.Clearance => isOnGround ? 900 : 600,
        AirportRadioServiceKind.Approach => isOnGround ? 850 : 980,
        AirportRadioServiceKind.Departure => isOnGround ? 800 : 930,
        AirportRadioServiceKind.Information => 750,
        AirportRadioServiceKind.ControlledOther => 700,
        _ => 0,
    };

    private static string BuildReason(AirportRadioServiceKind kind) => kind switch
    {
        AirportRadioServiceKind.Tower => "Tour prioritaire lorsqu'elle existe.",
        AirportRadioServiceKind.Ground => "Fréquence Sol disponible.",
        AirportRadioServiceKind.Clearance => "Fréquence Clairance disponible.",
        AirportRadioServiceKind.Approach => "Approche retenue en l'absence de Tour plus pertinente.",
        AirportRadioServiceKind.Departure => "Départ disponible.",
        AirportRadioServiceKind.Information => "AFIS/FSS disponible.",
        AirportRadioServiceKind.ControlledOther => "Service contrôlé disponible.",
        _ => "Aucune fréquence dialoguée fiable.",
    };

    private static string NormalizeName(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Phonie.Core;

public static partial class PilotIntentParser
{
    public static PilotIntent Parse(string? text) => ParseDetailed(text).Intent;

    public static PilotIntentDetails ParseDetailed(string? text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new PilotIntentDetails(PilotIntent.Unknown, null, false, false);
        }

        var reportedPoint = ExtractReportedPoint(normalized);
        var mentionsIntersection = ContainsAny(normalized, "intersection", "bretelle intermediaire", "depart intersection");
        var mentionsBacktrack = ContainsAny(normalized, "remontee de piste", "remonter la piste", "backtrack");

        if (ContainsAny(normalized, "repetez", "repeter", "say again", "de nouveau"))
        {
            return Details(PilotIntent.RepeatRequest);
        }

        if (mentionsBacktrack && ContainsAny(normalized, "demande", "pret", "pouvons"))
        {
            return Details(PilotIntent.BacktrackRequest);
        }

        // Une demande de roulage vers le point d'attente reste une demande de roulage.
        // Elle doit être évaluée avant « au point d'attente », sinon une phrase comme
        // « pour rouler jusqu'au point d'attente » devient à tort ReadyAtHoldShort.
        if (ContainsAny(normalized, "roulage", "rouler", "pret a rouler", "pret au roulage", "consigne de roulage"))
        {
            return Details(PilotIntent.TaxiRequest);
        }

        var lineUp = ContainsAny(normalized, "alignement", "m aligner", "nous aligner", "aligner", "aligne pret");
        var takeoff = ContainsAny(normalized, "decollage", "decoller", "decolle", "je decolle", "depart immediat", "autorisation de depart");
        if (lineUp && takeoff)
        {
            return Details(PilotIntent.LineUpAndTakeoffRequest);
        }

        var departureContext = ContainsAny(
            normalized,
            "pour un depart",
            "pour depart",
            "depart depuis l intersection",
            "depart de l intersection");
        var ready = ContainsAny(
                normalized,
                "pret au point d attente",
                "au point d attente",
                "pret au depart",
                "pret pour un depart",
                "pret pour depart",
                "pare au depart",
                "pare pour un depart")
            || (normalized.Contains("pret", StringComparison.Ordinal)
                && reportedPoint is not null)
            || (reportedPoint is not null && mentionsIntersection && departureContext)
            || (mentionsIntersection && departureContext)
            || (normalized.Contains("point d attente", StringComparison.Ordinal) && departureContext);
        if (ready && mentionsIntersection)
        {
            return Details(PilotIntent.ReadyForIntersectionDeparture);
        }

        if (ready)
        {
            return Details(PilotIntent.ReadyAtHoldShort);
        }

        if (lineUp)
        {
            return Details(PilotIntent.LineUpRequest);
        }

        if (takeoff)
        {
            return Details(PilotIntent.TakeoffRequest);
        }

        if (ContainsAny(normalized, "mise en route", "demarrage", "demarrer moteur"))
        {
            return Details(PilotIntent.StartupRequest);
        }

        // Toutes les salutations ouvrant ou reprenant un contact doivent atteindre
        // GroundOperationsEngine. Sinon DetectGreeting et ContainsReturnGreeting
        // resteraient inaccessibles pour « bonsoir » et « de retour ».
        if (ContainsAny(
                normalized,
                "bonjour",
                "bonsoir",
                "rebonjour",
                "re bonjour",
                "de retour",
                "retour avec vous",
                "premier contact",
                "au parking",
                "avec information"))
        {
            return Details(PilotIntent.InitialContact);
        }

        if (ContainsAny(normalized, "roger", "recu", "je roule", "je maintiens", "je rappelle", "wilco"))
        {
            return Details(PilotIntent.Readback);
        }

        return Details(PilotIntent.Unknown);

        PilotIntentDetails Details(PilotIntent intent) =>
            new(intent, reportedPoint, mentionsIntersection, mentionsBacktrack);
    }

    private static string? ExtractReportedPoint(string text)
    {
        var match = ContextualPointRegex().Match(text);
        if (!match.Success)
        {
            match = NumberedPointRegex().Match(text);
        }

        if (!match.Success)
        {
            return null;
        }

        var letter = match.Groups["letter"].Value switch
        {
            "alpha" => "A",
            "bravo" => "B",
            "charlie" => "C",
            "delta" => "D",
            "echo" => "E",
            "foxtrot" => "F",
            _ => match.Groups["letter"].Value.ToUpperInvariant(),
        };
        var number = match.Groups["number"].Value;
        return string.IsNullOrWhiteSpace(number) ? letter : letter + number;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
            }
        }

        return MultipleSpacesRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\b(?:pret\s+(?:en|a)|point\s+d\s+attente|intersection)\s+(?<letter>alpha|bravo|charlie|delta|echo|foxtrot|[a-f])\s*(?<number>[0-9]{0,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex ContextualPointRegex();

    [GeneratedRegex(@"\b(?<letter>alpha|bravo|charlie|delta|echo|foxtrot|[a-f])\s*(?<number>[0-9]{1,2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedPointRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}

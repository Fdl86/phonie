using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Phonie.Core;

public static partial class PilotIntentParser
{
    public static PilotIntent Parse(string? text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return PilotIntent.Unknown;
        }

        if (ContainsAny(normalized, "repetez", "repeter", "say again", "de nouveau"))
        {
            return PilotIntent.RepeatRequest;
        }

        var lineUp = ContainsAny(normalized, "alignement", "m aligner", "nous aligner", "aligner");
        var takeoff = ContainsAny(normalized, "decollage", "decoller", "depart immediat", "autorisation de depart");
        if (lineUp && takeoff)
        {
            return PilotIntent.LineUpAndTakeoffRequest;
        }

        if (ContainsAny(normalized, "pret au point d attente", "au point d attente", "pret au depart"))
        {
            return PilotIntent.ReadyAtHoldShort;
        }

        if (lineUp)
        {
            return PilotIntent.LineUpRequest;
        }

        if (takeoff)
        {
            return PilotIntent.TakeoffRequest;
        }


        if (ContainsAny(normalized, "roulage", "rouler", "pret a rouler", "pret au roulage"))
        {
            return PilotIntent.TaxiRequest;
        }

        if (ContainsAny(normalized, "mise en route", "demarrage", "demarrer moteur"))
        {
            return PilotIntent.StartupRequest;
        }

        if (ContainsAny(normalized, "bonjour", "premier contact", "au parking", "avec information"))
        {
            return PilotIntent.InitialContact;
        }

        return PilotIntent.Unknown;
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

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}

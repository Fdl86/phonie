using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Phonie.Models;

namespace Phonie.Services;

public static partial class PhraseologyService
{
    private static readonly Dictionary<string, char> NatoLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 'A', ["bravo"] = 'B', ["charlie"] = 'C', ["delta"] = 'D', ["echo"] = 'E',
        ["foxtrot"] = 'F', ["fox"] = 'F', ["golf"] = 'G', ["hotel"] = 'H', ["india"] = 'I',
        ["juliett"] = 'J', ["juliet"] = 'J', ["kilo"] = 'K', ["lima"] = 'L', ["mike"] = 'M',
        ["november"] = 'N', ["oscar"] = 'O', ["papa"] = 'P', ["quebec"] = 'Q', ["romeo"] = 'R',
        ["sierra"] = 'S', ["tango"] = 'T', ["uniform"] = 'U', ["victor"] = 'V', ["whiskey"] = 'W',
        ["xray"] = 'X', ["x-ray"] = 'X', ["yankee"] = 'Y', ["zulu"] = 'Z',
    };

    public static PilotMessageAnalysis Analyze(string rawText)
    {
        var normalized = Normalize(rawText);
        var station = DetectStation(normalized);
        var callsign = DetectCallsign(rawText, normalized);
        var position = DetectPosition(normalized);
        var intention = DetectIntention(normalized);
        var atis = DetectAtis(normalized);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(callsign)) missing.Add("indicatif");
        if (string.IsNullOrWhiteSpace(station)) missing.Add("station appelée");
        if (string.IsNullOrWhiteSpace(intention)) missing.Add("intention");

        var recognized = 0;
        if (station is not null) recognized++;
        if (callsign is not null) recognized++;
        if (position is not null) recognized++;
        if (intention is not null) recognized++;
        if (atis is not null) recognized++;

        return new PilotMessageAnalysis(
            rawText.Trim(),
            normalized,
            station,
            callsign,
            position,
            intention,
            atis,
            normalized.Contains("bonjour", StringComparison.Ordinal) || position is not null,
            missing,
            recognized / 5.0);
    }

    public static string BuildFirstContactResponse(
        PilotMessageAnalysis analysis,
        OperationalFrequency frequency,
        AtisInformation? atis,
        SimulatorSnapshot? snapshot)
    {
        if (!frequency.DialogueAllowed)
        {
            return frequency.Kind switch
            {
                OperationalRadioKind.AutomaticBroadcast => "Aucune réponse : cette fréquence diffuse une information automatique.",
                OperationalRadioKind.RecordedMessage => "Aucune réponse : cette fréquence diffuse un message enregistré.",
                OperationalRadioKind.SelfInformation => "Aucune réponse PHONIE : fréquence d'auto-information.",
                _ => "Aucune réponse : le service radio n'autorise pas le dialogue.",
            };
        }

        if (analysis.Callsign is null)
        {
            return "Station appelante, Poitiers, répétez votre indicatif.";
        }

        if (analysis.Intention is null)
        {
            return $"{analysis.Callsign}, Poitiers, précisez vos intentions.";
        }

        if (frequency.Kind == OperationalRadioKind.InformationService)
        {
            return $"{analysis.Callsign}, Poitiers Information, bonjour. Transmettez votre position, altitude et destination.";
        }

        if (frequency.Kind != OperationalRadioKind.Controlled)
        {
            return $"{analysis.Callsign}, Poitiers, message reçu.";
        }

        var qnh = atis is not null
            ? Math.Round(atis.QnhHpa).ToString("F0", CultureInfo.InvariantCulture)
            : snapshot is not null && double.IsFinite(snapshot.QnhHpa)
                ? Math.Round(snapshot.QnhHpa).ToString("F0", CultureInfo.InvariantCulture)
                : null;
        var runway = atis?.Runway;

        if (analysis.Intention.Contains("tours de piste", StringComparison.OrdinalIgnoreCase)
            && snapshot?.IsOnGround != false)
        {
            if (!string.IsNullOrWhiteSpace(runway) && qnh is not null)
            {
                return $"{analysis.Callsign}, Poitiers Tour, bonjour. Roulez vers le point d'attente piste {runway}, QNH {qnh}.";
            }

            return $"{analysis.Callsign}, Poitiers Tour, bonjour. Maintenez position, paramètres en cours d'acquisition.";
        }

        return $"{analysis.Callsign}, Poitiers, bonjour. Message reçu, rappelez prêt au roulage.";
    }

    private static string Normalize(string value)
    {
        var decomposed = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        var normalized = builder.ToString().Normalize(NormalizationForm.FormC);
        normalized = Regex.Replace(normalized, "[^a-z0-9-]+", " ");
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private static string? DetectStation(string text)
    {
        if (text.Contains("poitiers tour", StringComparison.Ordinal)) return "Poitiers Tour";
        if (text.Contains("poitiers approche", StringComparison.Ordinal) || text.Contains("poitiers siv", StringComparison.Ordinal)) return "Poitiers Approche / SIV";
        if (text.Contains("poitiers", StringComparison.Ordinal)) return "Poitiers";
        return null;
    }

    private static string? DetectCallsign(string raw, string normalized)
    {
        var compact = DirectCallsignRegex().Match(raw.ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal));
        if (compact.Success)
        {
            return $"F-{compact.Groups[1].Value}";
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!NatoLetters.TryGetValue(tokens[index], out var first) || first != 'F')
            {
                continue;
            }

            var letters = new List<char> { first };
            for (var next = index + 1; next < tokens.Length && letters.Count < 5; next++)
            {
                if (NatoLetters.TryGetValue(tokens[next], out var letter))
                {
                    letters.Add(letter);
                }
                else if (letters.Count > 1)
                {
                    break;
                }
            }

            if (letters.Count == 5 && letters[1] == 'G')
            {
                return $"F-G{letters[2]}{letters[3]}{letters[4]}";
            }
        }

        return null;
    }

    private static string? DetectPosition(string text)
    {
        if (text.Contains("parking aviation generale", StringComparison.Ordinal)) return "parking aviation générale";
        if (text.Contains("au parking", StringComparison.Ordinal) || text.Contains("parking", StringComparison.Ordinal)) return "parking";
        if (text.Contains("point d attente", StringComparison.Ordinal)) return "point d'attente";
        if (text.Contains("en finale", StringComparison.Ordinal) || text.Contains("finale", StringComparison.Ordinal)) return "finale";
        if (text.Contains("vent arriere", StringComparison.Ordinal)) return "vent arrière";
        return null;
    }

    private static string? DetectIntention(string text)
    {
        if (text.Contains("tour de piste", StringComparison.Ordinal) || text.Contains("tours de piste", StringComparison.Ordinal)) return "tours de piste";
        if (text.Contains("vol local", StringComparison.Ordinal) || text.Contains("local", StringComparison.Ordinal)) return "vol local";
        if (text.Contains("depart", StringComparison.Ordinal)) return "départ";
        if (text.Contains("atterrissage", StringComparison.Ordinal) || text.Contains("atterrir", StringComparison.Ordinal)) return "atterrissage";
        return null;
    }

    private static string? DetectAtis(string text)
    {
        foreach (var pair in NatoLetters)
        {
            if (text.Contains($"information {pair.Key}", StringComparison.Ordinal))
            {
                return pair.Value.ToString();
            }
        }

        return null;
    }

    [GeneratedRegex(@"F-?G([A-Z]{3})", RegexOptions.CultureInvariant)]
    private static partial Regex DirectCallsignRegex();
}

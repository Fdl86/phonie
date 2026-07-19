using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Phonie.Models;

namespace Phonie.Services;

public static partial class PhraseologyService
{
    private static readonly Dictionary<string, char> NatoLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 'A', ["alfa"] = 'A', ["bravo"] = 'B', ["charlie"] = 'C', ["charly"] = 'C',
        ["delta"] = 'D', ["echo"] = 'E', ["foxtrot"] = 'F', ["fox"] = 'F', ["effe"] = 'F',
        ["golf"] = 'G', ["golfe"] = 'G', ["hotel"] = 'H', ["india"] = 'I', ["juliett"] = 'J',
        ["juliet"] = 'J', ["kilo"] = 'K', ["lima"] = 'L', ["mike"] = 'M', ["november"] = 'N',
        ["oscar"] = 'O', ["papa"] = 'P', ["quebec"] = 'Q', ["romeo"] = 'R', ["sierra"] = 'S',
        ["tango"] = 'T', ["uniform"] = 'U', ["victor"] = 'V', ["whiskey"] = 'W', ["xray"] = 'X',
        ["x-ray"] = 'X', ["yankee"] = 'Y', ["zulu"] = 'Z',
    };

    private static readonly IReadOnlyDictionary<char, string> CanonicalNato = new Dictionary<char, string>
    {
        ['A'] = "alpha", ['B'] = "bravo", ['C'] = "charlie", ['D'] = "delta", ['E'] = "echo",
        ['F'] = "fox", ['G'] = "golf", ['H'] = "hotel", ['I'] = "india", ['J'] = "juliett",
        ['K'] = "kilo", ['L'] = "lima", ['M'] = "mike", ['N'] = "november", ['O'] = "oscar",
        ['P'] = "papa", ['Q'] = "quebec", ['R'] = "romeo", ['S'] = "sierra", ['T'] = "tango",
        ['U'] = "uniform", ['V'] = "victor", ['W'] = "whiskey", ['X'] = "xray", ['Y'] = "yankee",
        ['Z'] = "zulu",
    };

    public static PilotMessageAnalysis Analyze(string rawText, string? expectedCallsign = null)
    {
        var normalized = Normalize(rawText);
        var station = DetectStation(normalized);
        var callsignResult = DetectCallsign(rawText, normalized, expectedCallsign);
        var position = DetectPosition(normalized);
        var intention = DetectIntention(normalized);
        var atis = DetectAtis(normalized);
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(callsignResult.Callsign)) missing.Add("indicatif");
        if (string.IsNullOrWhiteSpace(station)) missing.Add("station appelée");
        if (string.IsNullOrWhiteSpace(intention)) missing.Add("intention");

        var recognized = 0.0;
        if (station is not null) recognized += 1.0;
        if (callsignResult.Callsign is not null) recognized += Math.Max(0.5, callsignResult.Confidence);
        if (position is not null) recognized += 1.0;
        if (intention is not null) recognized += 1.0;
        if (atis is not null) recognized += 1.0;

        return new PilotMessageAnalysis(
            rawText.Trim(),
            normalized,
            station,
            callsignResult.Callsign,
            NormalizeExpectedCallsign(expectedCallsign),
            callsignResult.Source,
            callsignResult.Confidence,
            position,
            intention,
            atis,
            normalized.Contains("bonjour", StringComparison.Ordinal) || position is not null,
            missing,
            Math.Clamp(recognized / 5.0, 0, 1));
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

        if ((analysis.Intention.Contains("tours de piste", StringComparison.OrdinalIgnoreCase)
             || analysis.Intention.Contains("roulage", StringComparison.OrdinalIgnoreCase))
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

    private static CallsignDetection DetectCallsign(string raw, string normalized, string? expectedCallsign)
    {
        var normalizedExpected = NormalizeExpectedCallsign(expectedCallsign);
        var expectedCompact = CompactCallsign(normalizedExpected);
        var rawUpper = raw.ToUpperInvariant();
        var rawCompact = new string(rawUpper.Where(char.IsAsciiLetterOrDigit).ToArray());

        if (!string.IsNullOrWhiteSpace(expectedCompact) && rawCompact.Contains(expectedCompact, StringComparison.Ordinal))
        {
            return new CallsignDetection(normalizedExpected, "SimConnect direct", 1.0);
        }

        var direct = DirectCallsignRegex().Match(rawUpper);
        if (direct.Success)
        {
            var compact = direct.Groups[1].Value + direct.Groups[2].Value;
            var formatted = FormatCallsign(compact);
            var confidence = string.IsNullOrWhiteSpace(expectedCompact)
                ? 1.0
                : Similarity(compact, expectedCompact);
            return new CallsignDetection(formatted, "immatriculation transcrite", confidence);
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!NatoLetters.TryGetValue(tokens[index], out var first))
            {
                continue;
            }

            var letters = new List<char> { first };
            for (var next = index + 1; next < tokens.Length && letters.Count < 6; next++)
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

            if (letters.Count >= 4)
            {
                var compact = new string(letters.ToArray());
                if (string.IsNullOrWhiteSpace(expectedCompact) || compact.Equals(expectedCompact, StringComparison.Ordinal))
                {
                    return new CallsignDetection(FormatCallsign(compact), "alphabet aéronautique", 1.0);
                }

                var confidence = Similarity(compact, expectedCompact);
                if (confidence >= 0.80)
                {
                    return new CallsignDetection(normalizedExpected, "alphabet rapproché de l'ATC ID", confidence);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedCompact))
        {
            var fuzzy = DetectExpectedCallsignFuzzy(tokens, expectedCompact);
            if (fuzzy >= 0.68)
            {
                return new CallsignDetection(normalizedExpected, "rapproché de l'ATC ID SimConnect", fuzzy);
            }
        }

        return new CallsignDetection(null, "non détecté", 0);
    }

    private static double DetectExpectedCallsignFuzzy(IReadOnlyList<string> tokens, string expectedCompact)
    {
        var expectedSpoken = string.Concat(expectedCompact.Select(character =>
            CanonicalNato.TryGetValue(character, out var word) ? word : character.ToString().ToLowerInvariant()));
        var best = 0.0;

        for (var start = 0; start < tokens.Count; start++)
        {
            var trigger = tokens[start].Replace("-", string.Empty, StringComparison.Ordinal);
            if (!trigger.Contains("fox", StringComparison.Ordinal)
                && !trigger.Contains("foxt", StringComparison.Ordinal)
                && !trigger.Equals("effe", StringComparison.Ordinal)
                && !trigger.Equals("f", StringComparison.Ordinal))
            {
                continue;
            }

            for (var length = 1; length <= 8 && start + length <= tokens.Count; length++)
            {
                var candidate = string.Concat(tokens.Skip(start).Take(length))
                    .Replace("-", string.Empty, StringComparison.Ordinal);
                var score = Similarity(candidate, expectedSpoken);
                if (score > best)
                {
                    best = score;
                }
            }
        }

        return best;
    }

    private static string? NormalizeExpectedCallsign(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = CompactCallsign(value);
        return compact.Length >= 4 ? FormatCallsign(compact) : null;
    }

    private static string CompactCallsign(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    private static string FormatCallsign(string compact)
    {
        compact = CompactCallsign(compact);
        return compact.Length >= 4 && char.IsAsciiLetter(compact[0])
            ? $"{compact[0]}-{compact[1..]}"
            : compact;
    }

    private static double Similarity(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0;
        }

        left = Normalize(left).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        right = Normalize(right).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                current[column] = left[row - 1] == right[column - 1]
                    ? previous[column - 1] + 1
                    : Math.Max(previous[column], current[column - 1]);
            }

            (previous, current) = (current, previous);
            Array.Clear(current, 0, current.Length);
        }

        return 2.0 * previous[right.Length] / (left.Length + right.Length);
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
        if (text.Contains("demande roulage", StringComparison.Ordinal) || text.Contains("devente de roulage", StringComparison.Ordinal) || text.Contains("pret a rouler", StringComparison.Ordinal) || text.Contains("roulage", StringComparison.Ordinal)) return "roulage";
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

    [GeneratedRegex(@"\b([A-Z])-?([A-Z0-9]{3,5})\b", RegexOptions.CultureInvariant)]
    private static partial Regex DirectCallsignRegex();

    private sealed record CallsignDetection(string? Callsign, string Source, double Confidence);
}

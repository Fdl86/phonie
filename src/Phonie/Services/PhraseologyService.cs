using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Phonie.Models;

namespace Phonie.Services;

public static partial class PhraseologyService
{
    private static readonly Dictionary<string, char> NatoLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 'A', ["alfa"] = 'A', ["fabre"] = 'A', ["va"] = 'A',
        ["bravo"] = 'B', ["marvo"] = 'B', ["beauchardie"] = 'B',
        ["charlie"] = 'C', ["charly"] = 'C',
        ["delta"] = 'D',
        ["echo"] = 'E',
        ["foxtrot"] = 'F', ["fox"] = 'F', ["effe"] = 'F',
        ["golf"] = 'G', ["golfe"] = 'G', ["gold"] = 'G', ["colf"] = 'G',
        ["hotel"] = 'H',
        ["india"] = 'I',
        ["juliett"] = 'J', ["juliet"] = 'J',
        ["kilo"] = 'K',
        ["lima"] = 'L',
        ["mike"] = 'M',
        ["november"] = 'N', ["novembre"] = 'N', ["novembres"] = 'N',
        ["oscar"] = 'O',
        ["papa"] = 'P',
        ["quebec"] = 'Q',
        ["romeo"] = 'R',
        ["sierra"] = 'S',
        ["tango"] = 'T',
        ["uniform"] = 'U',
        ["victor"] = 'V',
        ["whiskey"] = 'W',
        ["xray"] = 'X', ["x-ray"] = 'X',
        ["yankee"] = 'Y', ["yankees"] = 'Y', ["yanki"] = 'Y', ["yankis"] = 'Y', ["onki"] = 'Y', ["onqui"] = 'Y',
        ["zulu"] = 'Z',
    };

    private static readonly IReadOnlyDictionary<char, string[]> NatoAliases = new Dictionary<char, string[]>
    {
        ['A'] = ["alpha", "alfa", "fabre", "va"],
        ['B'] = ["bravo", "marvo", "beauchardie"],
        ['C'] = ["charlie", "charly"],
        ['D'] = ["delta"],
        ['E'] = ["echo"],
        ['F'] = ["fox", "foxtrot", "effe"],
        ['G'] = ["golf", "golfe", "gold", "colf"],
        ['H'] = ["hotel"],
        ['I'] = ["india"],
        ['J'] = ["juliett", "juliet"],
        ['K'] = ["kilo"],
        ['L'] = ["lima"],
        ['M'] = ["mike"],
        ['N'] = ["november", "novembre", "novembres"],
        ['O'] = ["oscar"],
        ['P'] = ["papa"],
        ['Q'] = ["quebec"],
        ['R'] = ["romeo"],
        ['S'] = ["sierra"],
        ['T'] = ["tango"],
        ['U'] = ["uniform"],
        ['V'] = ["victor"],
        ['W'] = ["whiskey"],
        ['X'] = ["xray"],
        ['Y'] = ["yankee", "yankees", "yanki", "yankis", "onki", "onqui"],
        ['Z'] = ["zulu"],
    };

    private static readonly HashSet<string> CallsignNoiseWords = new(StringComparer.Ordinal)
    {
        "a", "au", "aux", "avec", "bonjour", "de", "des", "du", "en", "et", "la", "le", "les", "un", "une",
    };

    private static readonly IReadOnlyDictionary<string, string[]> FusedCallsignTokens = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["foxgolf"] = ["fox", "golf"],
        ["foxgolfe"] = ["fox", "golfe"],
        ["foxgold"] = ["fox", "gold"],
        ["foxcolf"] = ["fox", "colf"],
        ["foxtrotgolf"] = ["foxtrot", "golf"],
    };

    public static PilotMessageAnalysis Analyze(string rawText, string? expectedCallsign = null)
    {
        var normalized = Normalize(rawText);
        var station = DetectStation(normalized);
        var callsignResult = DetectCallsign(rawText, normalized, expectedCallsign, station);
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

    private static CallsignDetection DetectCallsign(
        string raw,
        string normalized,
        string? expectedCallsign,
        string? detectedStation)
    {
        var normalizedExpected = NormalizeExpectedCallsign(expectedCallsign);
        var expectedCompact = CompactCallsign(normalizedExpected);
        var rawUpper = raw.ToUpperInvariant();
        var rawCompact = new string(rawUpper.Where(char.IsAsciiLetterOrDigit).ToArray());

        if (!string.IsNullOrWhiteSpace(expectedCompact) && rawCompact.Contains(expectedCompact, StringComparison.Ordinal))
        {
            return new CallsignDetection(normalizedExpected, "ATC ID SimConnect transcrit directement", 1.0);
        }

        var direct = DirectCallsignRegex().Match(rawUpper);
        if (direct.Success)
        {
            var compact = direct.Groups[1].Value + direct.Groups[2].Value;
            var formatted = FormatCallsign(compact);
            if (string.IsNullOrWhiteSpace(expectedCompact))
            {
                return new CallsignDetection(formatted, "immatriculation transcrite avec tiret", 1.0);
            }

            var confidence = Similarity(compact, expectedCompact);
            if (confidence >= 0.80)
            {
                return new CallsignDetection(normalizedExpected, "immatriculation rapprochée de l'ATC ID", confidence);
            }
        }

        var callsignText = RemoveStationContext(normalized, detectedStation);
        var tokens = TokenizeCallsignText(callsignText);

        if (!string.IsNullOrWhiteSpace(expectedCompact))
        {
            var expectedScore = DetectExpectedCallsignPhonetically(tokens, expectedCompact);
            if (expectedScore >= 0.68)
            {
                return new CallsignDetection(normalizedExpected, "alphabet rapproché de l'ATC ID SimConnect", expectedScore);
            }

            // Quand un ATC ID fiable existe, PHONIE ne fabrique jamais un autre indicatif à partir d'un mot ordinaire.
            return new CallsignDetection(null, "ATC ID non confirmé par la transcription", expectedScore);
        }

        var spoken = DetectSpokenFrenchRegistration(tokens);
        return spoken is null
            ? new CallsignDetection(null, "non détecté", 0)
            : new CallsignDetection(FormatCallsign(spoken), "alphabet aéronautique", 1.0);
    }

    private static string RemoveStationContext(string normalized, string? detectedStation)
    {
        var text = normalized;
        if (!string.IsNullOrWhiteSpace(detectedStation))
        {
            text = detectedStation switch
            {
                "Poitiers Tour" => text.Replace("poitiers tour", " ", StringComparison.Ordinal),
                "Poitiers Approche / SIV" => text
                    .Replace("poitiers approche", " ", StringComparison.Ordinal)
                    .Replace("poitiers siv", " ", StringComparison.Ordinal),
                "Poitiers" => text.Replace("poitiers", " ", StringComparison.Ordinal),
                _ => text,
            };
        }

        return Regex.Replace(text, "\\s+", " ").Trim();
    }

    private static IReadOnlyList<string> TokenizeCallsignText(string text)
    {
        var tokens = new List<string>();
        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var part in token.Split('-', StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = part.Trim();
                if (cleaned.Length == 0)
                {
                    continue;
                }

                if (TrySplitFusedCallsignToken(cleaned, out var fusedParts))
                {
                    tokens.AddRange(fusedParts);
                }
                else
                {
                    tokens.Add(cleaned);
                }
            }
        }

        return tokens;
    }

    private static bool TrySplitFusedCallsignToken(string token, out string[] parts)
    {
        var normalizedToken = token.Replace("-", string.Empty, StringComparison.Ordinal);
        if (FusedCallsignTokens.TryGetValue(normalizedToken, out var knownParts))
        {
            parts = knownParts;
            return true;
        }

        parts = Array.Empty<string>();
        return false;
    }

    private static double DetectExpectedCallsignPhonetically(IReadOnlyList<string> tokens, string expectedCompact)
    {
        if (expectedCompact.Length < 4 || tokens.Count == 0)
        {
            return 0;
        }

        var best = 0.0;
        for (var start = 0; start < tokens.Count; start++)
        {
            var firstScore = ScoreTokenForLetter(tokens[start], expectedCompact[0]);
            if (firstScore < 0.65)
            {
                continue;
            }

            var matched = 1;
            var totalScore = firstScore;
            var cursor = start + 1;

            for (var expectedIndex = 1; expectedIndex < expectedCompact.Length; expectedIndex++)
            {
                var bestTokenScore = 0.0;
                var bestTokenIndex = -1;
                var searchEnd = Math.Min(tokens.Count, cursor + 4);
                for (var tokenIndex = cursor; tokenIndex < searchEnd; tokenIndex++)
                {
                    if (CallsignNoiseWords.Contains(tokens[tokenIndex]))
                    {
                        continue;
                    }

                    var score = ScoreTokenForLetter(tokens[tokenIndex], expectedCompact[expectedIndex]);
                    if (score > bestTokenScore)
                    {
                        bestTokenScore = score;
                        bestTokenIndex = tokenIndex;
                    }
                }

                if (bestTokenIndex >= 0 && bestTokenScore >= 0.45)
                {
                    matched++;
                    totalScore += bestTokenScore;
                    cursor = bestTokenIndex + 1;
                }
            }

            var coverage = (double)matched / expectedCompact.Length;
            var averageQuality = totalScore / matched;
            var scoreForStart = (coverage * 0.75) + (averageQuality * 0.25);
            if (scoreForStart > best)
            {
                best = scoreForStart;
            }
        }

        return Math.Clamp(best, 0, 1);
    }

    private static double ScoreTokenForLetter(string token, char expectedLetter)
    {
        if (!NatoAliases.TryGetValue(expectedLetter, out var aliases))
        {
            return 0;
        }

        var normalizedToken = Normalize(token).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        if (normalizedToken.Length == 0)
        {
            return 0;
        }

        var best = 0.0;
        foreach (var alias in aliases)
        {
            if (normalizedToken.Equals(alias, StringComparison.Ordinal))
            {
                return 1.0;
            }

            if (normalizedToken.Contains(alias, StringComparison.Ordinal) || alias.Contains(normalizedToken, StringComparison.Ordinal))
            {
                best = Math.Max(best, 0.85);
            }

            best = Math.Max(best, Similarity(normalizedToken, alias));
        }

        return best;
    }

    private static string? DetectSpokenFrenchRegistration(IReadOnlyList<string> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!NatoLetters.TryGetValue(tokens[index], out var first) || first != 'F')
            {
                continue;
            }

            var letters = new List<char> { first };
            for (var next = index + 1; next < tokens.Count && letters.Count < 5; next++)
            {
                if (CallsignNoiseWords.Contains(tokens[next]))
                {
                    continue;
                }

                if (NatoLetters.TryGetValue(tokens[next], out var letter))
                {
                    letters.Add(letter);
                    continue;
                }

                if (letters.Count > 1)
                {
                    break;
                }
            }

            if (letters.Count == 5)
            {
                return new string(letters.ToArray());
            }
        }

        return null;
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
        var asksLineUp = text.Contains("alignement", StringComparison.Ordinal)
            || text.Contains("aligner", StringComparison.Ordinal);
        var asksTakeoff = text.Contains("decollage", StringComparison.Ordinal)
            || text.Contains("decoller", StringComparison.Ordinal);
        if (asksLineUp && asksTakeoff)
        {
            return "alignement et décollage";
        }

        if (asksLineUp)
        {
            return "alignement";
        }

        if (asksTakeoff)
        {
            return "décollage";
        }

        if (ToursDePisteRegex().IsMatch(text)
            || text.Contains("tourne piste", StringComparison.Ordinal)
            || text.Contains("tourne-piste", StringComparison.Ordinal))
        {
            return "tours de piste";
        }

        if (text.Contains("demande roulage", StringComparison.Ordinal)
            || text.Contains("devente de roulage", StringComparison.Ordinal)
            || text.Contains("consignes de rouleau", StringComparison.Ordinal)
            || text.Contains("pret a rouler", StringComparison.Ordinal)
            || text.Contains("roulage", StringComparison.Ordinal))
        {
            return "roulage";
        }

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

        if (text.Contains("information alpes", StringComparison.Ordinal)) return "A";
        return null;
    }

    [GeneratedRegex(@"\b([A-Z])\s*-\s*([A-Z0-9]{3,5})\b", RegexOptions.CultureInvariant)]
    private static partial Regex DirectCallsignRegex();

    [GeneratedRegex(@"\btours?\s+de\s+(?:piste|pistes|pisse|pisses)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ToursDePisteRegex();

    private sealed record CallsignDetection(string? Callsign, string Source, double Confidence);
}

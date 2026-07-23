using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Phonie.Core;

public sealed record RadioStationCandidate(
    string StationName,
    string StationKey,
    string ServiceRole,
    string AirportIcao,
    double FrequencyMhz,
    bool DialogueAllowed,
    string Scope = "Local");

public sealed record RadioStationCall(
    bool ExplicitlyCalled,
    bool MatchesActiveStation,
    bool IsOtherKnownStation,
    string? StationName,
    string? StationKey,
    string? ServiceRole,
    string? AirportIcao,
    double? FrequencyMhz,
    bool FrequencyUsable,
    double Confidence,
    string Reason);

public sealed record RadioUtteranceAnalysis(
    string RawText,
    string NormalizedText,
    string CorrectedText,
    RadioStationCall StationCall,
    PilotIntentDetails IntentDetails,
    string Greeting,
    IReadOnlyList<string> Corrections,
    double SemanticConfidence,
    string IntentSource);

public static partial class RadioUtteranceAnalyzer
{
    private static readonly HashSet<string> ServiceTokens = new(StringComparer.Ordinal)
    {
        "tour", "tours", "tower", "sol", "ground", "approche", "approach",
        "depart", "departure", "information", "info", "afis", "siv", "fis",
    };

    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "aerodrome", "aeroport", "airport", "de", "du", "des", "la", "le", "les",
        "secteur", "service", "centre", "controle", "control", "region", "regional",
    };

    public static RadioUtteranceAnalysis Analyze(
        string? rawText,
        RadioContext activeRadio,
        IReadOnlyList<RadioStationCandidate>? knownStations = null)
    {
        var raw = (rawText ?? string.Empty).Trim();
        var normalized = Normalize(raw);
        var corrections = new List<string>();
        var corrected = ApplyConservativeLexicalCorrections(normalized, corrections);
        var candidates = BuildCandidateSet(activeRadio, knownStations);
        var stationCall = ResolveStationCall(corrected, activeRadio, candidates);
        var intent = PilotIntentParser.ParseDetailed(corrected);
        var intentSource = "PilotIntentParser";

        var greeting = DetectGreeting(corrected);
        var confidence = intent.Intent == PilotIntent.Unknown ? 0.35 : 0.72;
        if (stationCall.ExplicitlyCalled)
        {
            confidence += stationCall.Confidence * 0.20;
        }
        else
        {
            confidence += 0.10;
        }
        if (corrections.Count == 0)
        {
            confidence += 0.05;
        }

        return new RadioUtteranceAnalysis(
            raw,
            normalized,
            corrected,
            stationCall,
            intent,
            greeting,
            corrections,
            Math.Clamp(confidence, 0, 1),
            intentSource);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var character in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return MultipleSpacesRegex().Replace(builder.ToString(), " ").Trim();
    }

    private static IReadOnlyList<RadioStationCandidate> BuildCandidateSet(
        RadioContext activeRadio,
        IReadOnlyList<RadioStationCandidate>? knownStations)
    {
        var candidates = new List<RadioStationCandidate>();
        if (knownStations is not null)
        {
            candidates.AddRange(knownStations.Where(item => !string.IsNullOrWhiteSpace(item.StationName)));
        }

        candidates.Add(new RadioStationCandidate(
            activeRadio.StationName,
            activeRadio.StationKey,
            activeRadio.ServiceRole,
            activeRadio.AirportIcao,
            activeRadio.FrequencyMhz,
            activeRadio.DialogueAllowed,
            activeRadio.Scope));

        return candidates
            .GroupBy(item => string.Join("|", item.StationKey, item.StationName, item.FrequencyMhz.ToString("F3", CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static RadioStationCall ResolveStationCall(
        string text,
        RadioContext activeRadio,
        IReadOnlyList<RadioStationCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None("Transmission vide.");
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var serviceIndex = FindServiceIndex(tokens);
        if (serviceIndex < 0 || serviceIndex > 5)
        {
            return None("Aucun suffixe de service explicite en début de transmission.");
        }

        var spokenService = CanonicalService(tokens[serviceIndex]);
        var prefixStart = Math.Max(0, serviceIndex - 3);
        var spokenPlaceTokens = tokens[prefixStart..serviceIndex]
            .Where(token => token.Length >= 2 && !NoiseTokens.Contains(token))
            .ToArray();
        if (spokenPlaceTokens.Length == 0)
        {
            return None("Suffixe de service sans toponyme distinctif : aucune station explicitement retenue.");
        }

        var spokenPlace = string.Join(' ', spokenPlaceTokens);
        var matches = new List<(RadioStationCandidate Candidate, double Score)>();
        foreach (var candidate in candidates)
        {
            var candidateService = CanonicalService(candidate.ServiceRole + " " + candidate.StationName);
            if (!ServicesCompatible(spokenService, candidateService))
            {
                continue;
            }

            var place = ExtractPlace(candidate.StationName);
            if (string.IsNullOrWhiteSpace(place))
            {
                continue;
            }

            var score = Math.Max(
                Similarity(spokenPlace, place),
                candidate.AirportIcao.Length > 0 ? Similarity(spokenPlace, candidate.AirportIcao) : 0);
            if (score >= 0.52)
            {
                matches.Add((candidate, score));
            }
        }

        if (matches.Count == 0)
        {
            return None("Le fragment capté ne correspond à aucune station connue : il est ignoré.");
        }

        var ordered = matches.OrderByDescending(item => item.Score).ToArray();
        var best = ordered[0];
        if (ordered.Length > 1 && best.Score - ordered[1].Score < 0.08
            && !string.Equals(best.Candidate.StationKey, ordered[1].Candidate.StationKey, StringComparison.OrdinalIgnoreCase))
        {
            return None("Appel de station ambigu : aucune incompatibilité n'est déduite.");
        }

        var matchingStationFrequencies = candidates
            .Where(item => string.Equals(item.StationKey, best.Candidate.StationKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Normalize(item.StationName), Normalize(best.Candidate.StationName), StringComparison.Ordinal))
            .Select(item => item.FrequencyMhz)
            .Where(value => double.IsFinite(value) && value > 0)
            .Distinct()
            .ToArray();
        var uniqueFrequency = matchingStationFrequencies.Length == 1 ? matchingStationFrequencies[0] : (double?)null;
        var activeMatch = StationKeysEqual(best.Candidate.StationKey, activeRadio.StationKey)
            || (string.Equals(Normalize(best.Candidate.StationName), Normalize(activeRadio.StationName), StringComparison.Ordinal)
                && ServicesCompatible(CanonicalService(best.Candidate.ServiceRole), CanonicalService(activeRadio.ServiceRole)));

        return new RadioStationCall(
            true,
            activeMatch,
            !activeMatch,
            best.Candidate.StationName,
            best.Candidate.StationKey,
            best.Candidate.ServiceRole,
            best.Candidate.AirportIcao,
            uniqueFrequency,
            uniqueFrequency.HasValue && best.Candidate.DialogueAllowed,
            best.Score,
            activeMatch
                ? "La station explicitement appelée correspond à la fréquence active."
                : "Une autre station connue est explicitement appelée avec une confiance suffisante.");
    }

    private static string ApplyConservativeLexicalCorrections(string text, ICollection<string> corrections)
    {
        var result = text;
        result = ReplaceContextual(result, @"\bpreaut\s+(?=roulage\b)", "pret ", "preaut -> prêt devant roulage", corrections);
        result = ReplaceContextual(result, @"\bapres\s+(?=(?:en\s+)?(?:alpha|bravo|charlie|delta|echo|foxtrot|[a-f])\s*\d{1,2}\b)", "pret ", "après -> prêt devant un point d'attente", corrections);
        result = ReplaceContextual(result, @"\bypres\s+(?=(?:en\s+)?(?:alpha|bravo|charlie|delta|echo|foxtrot|[a-f])\s*\d{1,2}\b)", "pret ", "Ypres -> prêt devant un point d'attente", corrections);
        return MultipleSpacesRegex().Replace(result, " ").Trim();
    }

    private static string ReplaceContextual(
        string input,
        string pattern,
        string replacement,
        string description,
        ICollection<string> corrections)
    {
        var output = Regex.Replace(input, pattern, replacement, RegexOptions.CultureInvariant);
        if (!string.Equals(input, output, StringComparison.Ordinal))
        {
            corrections.Add(description);
        }
        return output;
    }

    private static int FindServiceIndex(IReadOnlyList<string> tokens)
    {
        for (var index = 0; index < tokens.Count && index <= 5; index++)
        {
            if (ServiceTokens.Contains(tokens[index]))
            {
                return index;
            }
        }
        return -1;
    }

    private static string ExtractPlace(string stationName)
    {
        var tokens = Normalize(stationName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !ServiceTokens.Contains(token) && !NoiseTokens.Contains(token) && !token.All(char.IsDigit))
            .ToArray();
        return string.Join(' ', tokens);
    }

    private static string CanonicalService(string value)
    {
        var tokens = Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(token => token is "tour" or "tours" or "tower" or "twr")) return "TOWER";
        if (tokens.Any(token => token is "sol" or "ground" or "gnd")) return "GROUND";
        if (tokens.Any(token => token is "approche" or "approach" or "app")) return "APPROACH";
        if (tokens.Any(token => token is "depart" or "departure" or "dep")) return "DEPARTURE";
        if (tokens.Any(token => token is "information" or "info" or "afis" or "siv" or "fis")) return "INFORMATION";
        return string.Empty;
    }

    private static bool ServicesCompatible(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        return (left == "APPROACH" && right == "DEPARTURE")
            || (left == "DEPARTURE" && right == "APPROACH");
    }

    private static bool StationKeysEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string DetectGreeting(string text)
    {
        if (text.Contains("bonsoir", StringComparison.Ordinal)) return "bonsoir";
        if (text.Contains("rebonjour", StringComparison.Ordinal)
            || text.Contains("re bonjour", StringComparison.Ordinal)
            || text.Contains("de retour", StringComparison.Ordinal)
            || text.Contains("retour avec vous", StringComparison.Ordinal)) return "rebonjour";
        return text.Contains("bonjour", StringComparison.Ordinal) ? "bonjour" : string.Empty;
    }

    private static double Similarity(string left, string right)
    {
        left = Normalize(left).Replace(" ", string.Empty, StringComparison.Ordinal);
        right = Normalize(right).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        var distance = previous[right.Length];
        var levenshtein = 1.0 - (double)distance / Math.Max(left.Length, right.Length);

        var lcsPrevious = new int[right.Length + 1];
        var lcsCurrent = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            for (var column = 1; column <= right.Length; column++)
            {
                lcsCurrent[column] = left[row - 1] == right[column - 1]
                    ? lcsPrevious[column - 1] + 1
                    : Math.Max(lcsPrevious[column], lcsCurrent[column - 1]);
            }
            (lcsPrevious, lcsCurrent) = (lcsCurrent, lcsPrevious);
            Array.Clear(lcsCurrent, 0, lcsCurrent.Length);
        }
        var lcs = 2.0 * lcsPrevious[right.Length] / (left.Length + right.Length);
        return Math.Max(levenshtein, lcs);
    }

    private static RadioStationCall None(string reason) => new(
        false,
        false,
        false,
        null,
        null,
        null,
        null,
        null,
        false,
        0,
        reason);

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleSpacesRegex();
}

namespace Phonie.Core;

public static class CallsignFormatter
{
    private static readonly IReadOnlyDictionary<char, string> Phonetic = new Dictionary<char, string>
    {
        ['A'] = "Alpha",
        ['B'] = "Bravo",
        ['C'] = "Charlie",
        ['D'] = "Delta",
        ['E'] = "Echo",
        ['F'] = "Fox",
        ['G'] = "Golf",
        ['H'] = "Hôtel",
        ['I'] = "India",
        ['J'] = "Juliett",
        ['K'] = "Kilo",
        ['L'] = "Lima",
        ['M'] = "Mike",
        ['N'] = "Novembre",
        ['O'] = "Oscar",
        ['P'] = "Papa",
        ['Q'] = "Québec",
        ['R'] = "Roméo",
        ['S'] = "Sierra",
        ['T'] = "Tango",
        ['U'] = "Uniform",
        ['V'] = "Victor",
        ['W'] = "Whiskey",
        ['X'] = "X-ray",
        ['Y'] = "Yankee",
        ['Z'] = "Zulu",
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var compact = new string(value.Trim().ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (compact.Length < 4 || !char.IsAsciiLetter(compact[0]))
        {
            return string.Empty;
        }

        return $"{compact[0]}-{compact[1..]}";
    }

    public static string BuildShort(string? fullCallsign)
    {
        var normalized = Normalize(fullCallsign);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var compact = normalized.Replace("-", string.Empty, StringComparison.Ordinal);
        if (compact.Length < 3)
        {
            return normalized;
        }

        return $"{compact[0]}-{compact[^2..]}";
    }

    public static string SpeakFull(string? callsign)
    {
        var normalized = Normalize(callsign);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return string.Join(" ", normalized.Replace("-", string.Empty, StringComparison.Ordinal).Select(SpeakCharacter));
    }

    public static string SpeakLetter(char value) => SpeakCharacter(char.ToUpperInvariant(value));

    public static string SpeakShort(string? callsign)
    {
        var shortCallsign = BuildShort(callsign);
        if (string.IsNullOrWhiteSpace(shortCallsign))
        {
            return string.Empty;
        }

        return string.Join(" ", shortCallsign.Replace("-", string.Empty, StringComparison.Ordinal).Select(SpeakCharacter));
    }

    private static string SpeakCharacter(char value) =>
        Phonetic.TryGetValue(value, out var word) ? word : value.ToString();
}

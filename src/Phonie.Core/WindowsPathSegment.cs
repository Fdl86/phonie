using System.Security.Cryptography;
using System.Text;

namespace Phonie.Core;

public static class WindowsPathSegment
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string? value, string fallback = "station", int maxLength = 80)
    {
        if (maxLength < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        var source = (value ?? string.Empty).Trim();
        var builder = new StringBuilder(source.Length);
        foreach (var character in source)
        {
            builder.Append(character < 32 || character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*'
                ? '_'
                : character);
        }

        var clean = builder.ToString().TrimEnd(' ', '.');
        if (clean is "." or ".." || string.IsNullOrWhiteSpace(clean))
        {
            clean = fallback;
        }

        var dotIndex = clean.IndexOf('.');
        var baseName = dotIndex >= 0 ? clean[..dotIndex] : clean;
        if (ReservedNames.Contains(baseName))
        {
            clean = "_" + clean;
        }

        if (clean.Length > maxLength)
        {
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clean)))[..10].ToLowerInvariant();
            clean = $"{clean[..(maxLength - suffix.Length - 1)]}-{suffix}";
        }

        return clean;
    }
}

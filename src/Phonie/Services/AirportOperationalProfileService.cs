using System.Text.Json;
using System.Text.Json.Serialization;
using Phonie.Core;

namespace Phonie.Services;

public sealed class AirportOperationalProfileService
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AirportOperationalProfile? Load(string? icao)
    {
        if (string.IsNullOrWhiteSpace(icao))
        {
            return null;
        }

        var normalized = icao.Trim().ToUpperInvariant();
        var path = Path.Combine(AppPaths.AirportProfilesDirectory, $"{normalized}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var profile = JsonSerializer.Deserialize<AirportOperationalProfile>(
                File.ReadAllText(path),
                this.jsonOptions);
            return profile is not null
                && string.Equals(profile.Icao, normalized, StringComparison.OrdinalIgnoreCase)
                ? profile
                : null;
        }
        catch
        {
            return null;
        }
    }
}

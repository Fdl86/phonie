namespace Phonie.Core;

public sealed record NearbyAirportCandidate(
    string Icao,
    string Region,
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    IReadOnlyList<double> FrequenciesMhz);

public sealed record AirportContextSelection(
    string GeographicIcao,
    double GeographicDistanceNm,
    string RadioIcao,
    string RadioSource);

public static class AirportContextResolver
{
    private const double EarthRadiusNm = 3440.065;
    private const double GeographicGroundRadiusNm = 10.0;
    private const double GeographicAirRadiusNm = 8.0;
    private const double GeographicHysteresisNm = 2.0;
    private const double RadioStationMatchRadiusNm = 5.0;
    private const double FrequencyToleranceMhz = 0.0021;

    public static AirportContextSelection Resolve(
        IReadOnlyList<NearbyAirportCandidate> airports,
        double aircraftLatitude,
        double aircraftLongitude,
        bool isOnGround,
        string? previousGeographicIcao,
        string? stationIdent,
        double activeFrequencyMhz,
        double? stationLatitude,
        double? stationLongitude)
    {
        airports ??= Array.Empty<NearbyAirportCandidate>();

        var validAirports = airports
            .Where(item => IsUsableIcao(item.Icao)
                && double.IsFinite(item.Latitude)
                && double.IsFinite(item.Longitude))
            .Select(item => new
            {
                Airport = item,
                Distance = DistanceNm(aircraftLatitude, aircraftLongitude, item.Latitude, item.Longitude),
            })
            .OrderBy(item => item.Distance)
            .ToArray();

        var geographicLimit = isOnGround ? GeographicGroundRadiusNm : GeographicAirRadiusNm;
        var nearest = validAirports.FirstOrDefault();
        var geographicIcao = nearest is not null && nearest.Distance <= geographicLimit
            ? NormalizeIcao(nearest.Airport.Icao)
            : string.Empty;
        var geographicDistance = nearest is not null && nearest.Distance <= geographicLimit
            ? nearest.Distance
            : double.NaN;

        var previous = validAirports.FirstOrDefault(item =>
            string.Equals(item.Airport.Icao, previousGeographicIcao, StringComparison.OrdinalIgnoreCase));
        if (previous is not null
            && previous.Distance <= geographicLimit + GeographicHysteresisNm
            && (nearest is null || previous.Distance <= nearest.Distance + 0.6))
        {
            geographicIcao = NormalizeIcao(previous.Airport.Icao);
            geographicDistance = previous.Distance;
        }

        var normalizedStation = NormalizeIcao(stationIdent);
        if (isOnGround
            && string.IsNullOrWhiteSpace(geographicIcao)
            && IsUsableIcao(normalizedStation)
            && stationLatitude.HasValue
            && stationLongitude.HasValue)
        {
            var stationDistanceFromAircraft = DistanceNm(
                aircraftLatitude,
                aircraftLongitude,
                stationLatitude.Value,
                stationLongitude.Value);
            if (double.IsFinite(stationDistanceFromAircraft)
                && stationDistanceFromAircraft <= GeographicGroundRadiusNm)
            {
                geographicIcao = normalizedStation;
                geographicDistance = stationDistanceFromAircraft;
            }
        }

        if (IsUsableIcao(normalizedStation))
        {
            return new AirportContextSelection(
                geographicIcao,
                geographicDistance,
                normalizedStation,
                "Identifiant COM actif");
        }

        if (stationLatitude.HasValue
            && stationLongitude.HasValue
            && double.IsFinite(stationLatitude.Value)
            && double.IsFinite(stationLongitude.Value))
        {
            var stationAirport = validAirports
                .Select(item => new
                {
                    item.Airport,
                    Distance = DistanceNm(
                        stationLatitude.Value,
                        stationLongitude.Value,
                        item.Airport.Latitude,
                        item.Airport.Longitude),
                })
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            if (stationAirport is not null && stationAirport.Distance <= RadioStationMatchRadiusNm)
            {
                return new AirportContextSelection(
                    geographicIcao,
                    geographicDistance,
                    NormalizeIcao(stationAirport.Airport.Icao),
                    "Position de la station COM active");
            }
        }

        if (double.IsFinite(activeFrequencyMhz) && activeFrequencyMhz > 0)
        {
            var frequencyAirports = validAirports
                .Where(item => item.Airport.FrequenciesMhz.Any(value => Matches(value, activeFrequencyMhz)))
                .OrderBy(item => item.Distance)
                .ToArray();
            if (frequencyAirports.Length == 1)
            {
                return new AirportContextSelection(
                    geographicIcao,
                    geographicDistance,
                    NormalizeIcao(frequencyAirports[0].Airport.Icao),
                    "Fréquence COM unique dans les Facilities chargées");
            }
        }

        return new AirportContextSelection(
            geographicIcao,
            geographicDistance,
            string.Empty,
            "Station radio non résolue");
    }

    public static (double Latitude, double Longitude)? ProjectStationPosition(
        double aircraftLatitude,
        double aircraftLongitude,
        double bearingDegrees,
        double distanceMeters)
    {
        if (!double.IsFinite(aircraftLatitude)
            || !double.IsFinite(aircraftLongitude)
            || !double.IsFinite(bearingDegrees)
            || bearingDegrees < 0
            || !double.IsFinite(distanceMeters)
            || distanceMeters < 0)
        {
            return null;
        }

        var angularDistance = (distanceMeters / 1852.0) / EarthRadiusNm;
        var bearing = DegreesToRadians(bearingDegrees);
        var latitude1 = DegreesToRadians(aircraftLatitude);
        var longitude1 = DegreesToRadians(aircraftLongitude);

        var latitude2 = Math.Asin(
            (Math.Sin(latitude1) * Math.Cos(angularDistance))
            + (Math.Cos(latitude1) * Math.Sin(angularDistance) * Math.Cos(bearing)));
        var longitude2 = longitude1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angularDistance) * Math.Cos(latitude1),
            Math.Cos(angularDistance) - (Math.Sin(latitude1) * Math.Sin(latitude2)));

        var longitudeDegrees = ((RadiansToDegrees(longitude2) + 540.0) % 360.0) - 180.0;
        return (RadiansToDegrees(latitude2), longitudeDegrees);
    }

    public static double DistanceNm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        if (!double.IsFinite(latitude1)
            || !double.IsFinite(longitude1)
            || !double.IsFinite(latitude2)
            || !double.IsFinite(longitude2))
        {
            return double.NaN;
        }

        var lat1 = DegreesToRadians(latitude1);
        var lat2 = DegreesToRadians(latitude2);
        var deltaLat = DegreesToRadians(latitude2 - latitude1);
        var deltaLon = DegreesToRadians(longitude2 - longitude1);
        var a = (Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2))
            + (Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2));
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        return EarthRadiusNm * c;
    }

    private static bool Matches(double left, double right) =>
        double.IsFinite(left) && Math.Abs(left - right) <= FrequencyToleranceMhz;

    private static bool IsUsableIcao(string? value) =>
        value?.Length == 4 && value.All(char.IsAsciiLetterOrDigit);

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return IsUsableIcao(normalized) ? normalized : string.Empty;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}

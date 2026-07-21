namespace Phonie.Models;

public sealed record NearbyAirportData(
    string Icao,
    string Region,
    double Latitude,
    double Longitude,
    double AltitudeMeters);

public sealed record NearbyAirportSnapshot(
    DateTimeOffset Timestamp,
    IReadOnlyList<NearbyAirportData> Airports,
    string Status);

public sealed record AirportContextChanged(
    DateTimeOffset Timestamp,
    string GeographicIcao,
    double GeographicDistanceNm,
    string RadioIcao,
    string RadioSource,
    IReadOnlyList<NearbyAirportData> NearbyAirports);

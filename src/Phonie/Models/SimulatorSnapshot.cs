namespace Phonie.Models;

public sealed record SimulatorSnapshot(
    DateTimeOffset Timestamp,
    string Simulator,
    string AircraftTitle,
    double Latitude,
    double Longitude,
    double AltitudeFeet,
    double HeadingMagneticDegrees,
    double IndicatedAirspeedKnots,
    double GroundSpeedKnots,
    bool IsOnGround,
    double Com1ActiveMhz,
    double Com1StandbyMhz,
    string TransponderCode,
    double DistanceToLfbiNm);

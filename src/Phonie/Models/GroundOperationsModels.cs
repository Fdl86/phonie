namespace Phonie.Models;

public sealed record GroundTrafficContactData(
    uint ObjectId,
    string Callsign,
    double Latitude,
    double Longitude,
    double GroundSpeedKnots,
    bool IsOnGround,
    DateTimeOffset Timestamp);

public sealed record GroundTrafficSnapshot(
    DateTimeOffset Timestamp,
    bool ProviderAvailable,
    IReadOnlyList<GroundTrafficContactData> Contacts,
    string Status);

public sealed record GroundOperationsUiState(
    string Airport,
    string GraphStatus,
    string Position,
    string SessionState,
    string Runway,
    string HoldShort,
    string Route,
    string Occupancy,
    double Confidence,
    string LastDecision);

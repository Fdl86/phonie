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

public sealed record GroundMapNodeData(
    string Id,
    double X,
    double Z,
    string Kind,
    string Label,
    bool IsSelected,
    bool IsOccupied,
    bool IsAircraft);

public sealed record GroundMapEdgeData(
    uint Id,
    double FromX,
    double FromZ,
    double ToX,
    double ToZ,
    string Kind,
    string Name,
    bool IsRoute,
    bool IsOccupied,
    bool IsRunway);

public sealed record GroundMapTrafficData(
    uint ObjectId,
    string Callsign,
    double X,
    double Z,
    string Classification);

public sealed record GroundMapSnapshot(
    string Airport,
    IReadOnlyList<GroundMapNodeData> Nodes,
    IReadOnlyList<GroundMapEdgeData> Edges,
    IReadOnlyList<GroundMapTrafficData> Traffic,
    double? AircraftX,
    double? AircraftZ,
    double AircraftHeadingDegrees,
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
    string LastDecision,
    string Diagnostic,
    string ProfileStatus,
    string AcknowledgementStatus,
    GroundMapSnapshot Map);

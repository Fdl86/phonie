namespace Phonie.Core;

public enum TaxiPathKind
{
    Unknown = 0,
    Taxi = 1,
    Runway = 2,
    Parking = 3,
    Path = 4,
    Closed = 5,
    Vehicle = 6,
    Road = 7,
    PaintedLine = 8,
}

public enum GroundNodeKind
{
    TaxiPoint,
    HoldShort,
    Parking,
}

public enum GroundPositionKind
{
    Unknown,
    Parking,
    Taxiway,
    HoldShort,
    Runway,
    Airborne,
}

public enum GroundSessionState
{
    Unknown,
    Parked,
    StartupRequested,
    ReadyToTaxi,
    TaxiClearanceIssued,
    Taxiing,
    AtHoldShort,
    ReadyForDeparture,
    LineUpCleared,
    LinedUp,
    TakeoffCleared,
    Airborne,
}

public enum PilotIntent
{
    Unknown,
    InitialContact,
    StartupRequest,
    TaxiRequest,
    ReadyAtHoldShort,
    LineUpRequest,
    TakeoffRequest,
    LineUpAndTakeoffRequest,
    RepeatRequest,
    Readback,
}

public enum ControllerAction
{
    Silent,
    Speak,
    RequestClarification,
    Unable,
}

public enum ServiceCapability
{
    Unknown,
    Controlled,
    InformationOnly,
    AutomaticBroadcast,
    RecordedMessage,
    SelfInformation,
}

public enum OccupancyKnowledge
{
    Unknown,
    Available,
}

public sealed record FacilityRunway(
    uint Index,
    double Latitude,
    double Longitude,
    double HeadingDegrees,
    double LengthMeters,
    double WidthMeters,
    int Surface,
    int PrimaryNumber,
    int PrimaryDesignator,
    int SecondaryNumber,
    int SecondaryDesignator);

public sealed record FacilityTaxiPoint(
    uint Index,
    int Type,
    int Orientation,
    double BiasX,
    double BiasZ);

public sealed record FacilityParking(
    uint Index,
    int Type,
    int TaxiPointType,
    int Name,
    int Suffix,
    uint Number,
    int Orientation,
    double HeadingDegrees,
    double RadiusMeters,
    double BiasX,
    double BiasZ);

public sealed record FacilityTaxiPath(
    uint Index,
    int Type,
    double WidthMeters,
    uint WeightLimit,
    int RawRunwayNumber,
    int RawRunwayDesignator,
    int StartIndex,
    int EndIndex,
    uint NameIndex);

public sealed record FacilityTaxiName(uint Index, string Name);

public sealed record FacilityAirportSnapshot(
    string Icao,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyList<FacilityRunway> Runways,
    IReadOnlyList<FacilityTaxiPoint> TaxiPoints,
    IReadOnlyList<FacilityParking> Parkings,
    IReadOnlyList<FacilityTaxiPath> TaxiPaths,
    IReadOnlyList<FacilityTaxiName> TaxiNames);

public sealed record GroundNode(
    string Id,
    GroundNodeKind Kind,
    uint SourceIndex,
    double X,
    double Z,
    string Label);

public sealed record GroundEdge(
    uint SourceIndex,
    string FromNodeId,
    string ToNodeId,
    TaxiPathKind Kind,
    string TaxiwayName,
    double LengthMeters,
    double WidthMeters,
    uint WeightLimit,
    int? RunwayNumber,
    int? RunwayDesignator,
    bool Traversable,
    bool IsRunway,
    bool IsClosed);

public sealed record RunwayEnd(
    string Designator,
    int Number,
    int DesignatorCode,
    double HeadingDegrees,
    uint RunwayIndex,
    double RunwayLengthMeters);

public sealed record RunwaySelection(
    bool Success,
    RunwayEnd? RunwayEnd,
    string Reason,
    double Confidence);

public sealed record HoldShortPoint(
    string NodeId,
    string Label,
    uint? AssociatedRunwayIndex,
    int? NearestRunwayNumber,
    double DistanceToRunwayMeters);

public sealed record AirportGroundModel(
    string Icao,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyDictionary<string, GroundNode> Nodes,
    IReadOnlyList<GroundEdge> Edges,
    IReadOnlyList<RunwayEnd> RunwayEnds,
    IReadOnlyList<HoldShortPoint> HoldShortPoints,
    IReadOnlyList<string> Warnings)
{
    public bool IsUsable => Nodes.Count > 0 && Edges.Any(edge => edge.Traversable) && RunwayEnds.Count > 0;
}

public sealed record AircraftGroundObservation(
    DateTimeOffset Timestamp,
    double Latitude,
    double Longitude,
    double GroundSpeedKnots,
    bool IsOnGround,
    double HeadingDegrees);

public sealed record GroundTrafficContact(
    uint ObjectId,
    string Callsign,
    double Latitude,
    double Longitude,
    double GroundSpeedKnots,
    bool IsOnGround,
    DateTimeOffset Timestamp);

public sealed record GroundOccupancySnapshot(
    DateTimeOffset Timestamp,
    OccupancyKnowledge Knowledge,
    IReadOnlySet<string> OccupiedNodeIds,
    IReadOnlySet<uint> OccupiedEdgeIds,
    string Source)
{
    public static GroundOccupancySnapshot Unknown(DateTimeOffset timestamp, string source) =>
        new(timestamp, OccupancyKnowledge.Unknown, new HashSet<string>(), new HashSet<uint>(), source);
}

public sealed record GroundTrafficOccupancyDiagnostic(
    uint ObjectId,
    string Callsign,
    double GroundSpeedKnots,
    bool IsOnGround,
    string Classification,
    string? NearestParkingNodeId,
    double? NearestParkingDistanceMeters,
    uint? NearestEdgeId,
    double? NearestEdgeDistanceMeters,
    IReadOnlyList<string> OccupiedNodeIds,
    IReadOnlyList<uint> OccupiedEdgeIds,
    string Reason);

public sealed record GroundOccupancyBuildResult(
    GroundOccupancySnapshot Snapshot,
    IReadOnlyList<GroundTrafficOccupancyDiagnostic> Contacts);

public sealed record GroundLocation(
    GroundPositionKind Kind,
    string? NodeId,
    uint? EdgeId,
    double DistanceMeters,
    double Confidence,
    string Description);

public sealed record TaxiRoute(
    bool Success,
    string FailureReason,
    string StartNodeId,
    HoldShortPoint? HoldShort,
    RunwayEnd? Runway,
    IReadOnlyList<GroundEdge> Edges,
    IReadOnlyList<string> TaxiwayNames,
    double TotalDistanceMeters,
    double Confidence);

public sealed record RadioContext(
    ServiceCapability Capability,
    string StationName,
    bool DialogueAllowed,
    string Source);

public sealed record ControllerDecision(
    ControllerAction Action,
    string ReasonCode,
    string SpokenText,
    string SystemMessage,
    GroundSessionState StateBefore,
    GroundSessionState StateAfter,
    TaxiRoute? TaxiRoute,
    string FullCallsign,
    string ShortCallsign,
    double Confidence);

public sealed class GroundSession
{
    public string FullCallsign { get; set; } = string.Empty;

    public string AuthorizedShortCallsign { get; set; } = string.Empty;

    public bool ContactEstablished { get; set; }

    public GroundSessionState State { get; set; } = GroundSessionState.Unknown;

    public string LastPilotRequest { get; set; } = string.Empty;

    public string LastControllerInstruction { get; set; } = string.Empty;

    public RunwayEnd? AssignedRunway { get; set; }

    public HoldShortPoint? AssignedHoldShort { get; set; }

    public TaxiRoute? AssignedTaxiRoute { get; set; }
}

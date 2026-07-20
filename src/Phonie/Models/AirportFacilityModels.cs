namespace Phonie.Models;

public sealed class AirportFacilityReport
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string Source { get; init; } = "SimConnect Facilities API";

    public string Simulator { get; init; } = string.Empty;

    public string RequestedIcao { get; init; } = string.Empty;

    public string Icao { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; } = double.NaN;

    public double Longitude { get; set; } = double.NaN;

    public double AltitudeMeters { get; set; } = double.NaN;

    public float MagneticVariationDegrees { get; set; } = float.NaN;

    public int RunwayCountDeclared { get; set; }

    public int StartCountDeclared { get; set; }

    public int FrequencyCountDeclared { get; set; }

    public int TaxiPointCountDeclared { get; set; }

    public int TaxiParkingCountDeclared { get; set; }

    public int TaxiPathCountDeclared { get; set; }

    public int TaxiNameCountDeclared { get; set; }

    public List<AirportRunwayData> Runways { get; init; } = new();

    public List<AirportStartData> Starts { get; init; } = new();

    public List<AirportFrequencyData> Frequencies { get; init; } = new();

    public List<AirportTaxiParkingData> TaxiParkings { get; init; } = new();

    public List<AirportTaxiPointData> TaxiPoints { get; init; } = new();

    public List<AirportTaxiPathData> TaxiPaths { get; init; } = new();

    public List<AirportTaxiNameData> TaxiNames { get; init; } = new();

    public List<string> ParseWarnings { get; init; } = new();

    public List<FacilityPacketDiagnostic> PacketDiagnostics { get; init; } = new();

    public FacilityDiagnosticSummary DiagnosticSummary { get; set; } = new();

    public string DiagnosticDirectoryPath { get; set; } = string.Empty;

    public string JsonPath { get; set; } = string.Empty;

    public string TextPath { get; set; } = string.Empty;
}

public sealed record AirportRunwayData(
    uint Index,
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    float HeadingDegrees,
    float LengthMeters,
    float WidthMeters,
    float PatternAltitudeMeters,
    float Slope,
    int Surface,
    int PrimaryNumber,
    int PrimaryDesignator,
    int SecondaryNumber,
    int SecondaryDesignator);

public sealed record AirportStartData(
    uint Index,
    double Latitude,
    double Longitude,
    double AltitudeMeters,
    float HeadingDegrees,
    int Number,
    int Designator,
    int Type);

public sealed record AirportFrequencyData(
    uint Index,
    int Type,
    uint FrequencyHz,
    double FrequencyMhz,
    string Name);

public sealed record AirportTaxiParkingData(
    uint Index,
    int Type,
    int TaxiPointType,
    int Name,
    int Suffix,
    uint Number,
    int Orientation,
    float HeadingDegrees,
    float RadiusMeters,
    float BiasX,
    float BiasZ);

public sealed record AirportTaxiPointData(
    uint Index,
    int Type,
    int Orientation,
    float BiasX,
    float BiasZ);

public sealed record AirportTaxiPathData(
    uint Index,
    int Type,
    float WidthMeters,
    float LeftHalfWidthMeters,
    float RightHalfWidthMeters,
    uint WeightLimit,
    int RunwayNumber,
    int RunwayDesignator,
    int LeftEdge,
    bool LeftEdgeLighted,
    int RightEdge,
    bool RightEdgeLighted,
    bool CenterLine,
    bool CenterLineLighted,
    int StartIndex,
    int EndIndex,
    uint NameIndex);

public sealed record AirportTaxiNameData(uint Index, string Name);

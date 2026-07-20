namespace Phonie.Models;

public sealed record FacilityPacketEnvelope(
    uint DeclaredSize,
    uint Version,
    uint MessageId,
    uint UserRequestId,
    uint UniqueRequestId,
    uint ParentUniqueRequestId,
    int FacilityType,
    bool IsListItem,
    uint ItemIndex,
    uint ListSize,
    int ActualSize,
    int PayloadLength)
{
    public bool SizeMatches => this.DeclaredSize == this.ActualSize;
}

public sealed record FacilityFieldDiagnostic(
    string Name,
    int PacketOffset,
    int PayloadOffset,
    int Size,
    string DataType,
    string Value,
    string Hex);

public sealed class FacilityPacketDiagnostic
{
    public int Sequence { get; init; }

    public FacilityPacketEnvelope Envelope { get; init; } = new(0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0);

    public string FacilityTypeName { get; init; } = string.Empty;

    public string RawFile { get; set; } = string.Empty;

    public string HeaderHex { get; init; } = string.Empty;

    public string PayloadHexPreview { get; init; } = string.Empty;

    public bool ParseSucceeded { get; set; }

    public string ParseError { get; set; } = string.Empty;

    public string SemanticWarning { get; set; } = string.Empty;

    public List<FacilityFieldDiagnostic> Fields { get; init; } = new();
}

public sealed class FacilityDiagnosticSummary
{
    public int PacketCount { get; set; }

    public int TaxiPathPacketCount { get; set; }

    public int ParsedTaxiPathPacketCount { get; set; }

    public int FailedPacketCount { get; set; }

    public int SuspiciousTaxiPathCount { get; set; }

    public int SizeMismatchCount { get; set; }

    public int UnexpectedPayloadLengthCount { get; set; }

    public int DuplicateIndexCount { get; set; }

    public int MissingIndexCount { get; set; }

    public int OutOfRangeIndexCount { get; set; }

    public int InconsistentListSizeCount { get; set; }

    public int? FirstFailedTaxiPathSequence { get; set; }

    public uint? FirstFailedTaxiPathIndex { get; set; }

    public int? FirstSuspiciousTaxiPathSequence { get; set; }

    public uint? FirstSuspiciousTaxiPathIndex { get; set; }

    public bool TaxiPathBinaryLayoutValidated =>
        this.TaxiPathPacketCount > 0
        && this.ParsedTaxiPathPacketCount == this.TaxiPathPacketCount
        && this.SuspiciousTaxiPathCount == 0
        && this.SizeMismatchCount == 0
        && this.UnexpectedPayloadLengthCount == 0
        && this.DuplicateIndexCount == 0
        && this.MissingIndexCount == 0
        && this.OutOfRangeIndexCount == 0
        && this.InconsistentListSizeCount == 0;
}

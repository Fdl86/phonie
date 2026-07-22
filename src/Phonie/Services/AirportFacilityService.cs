using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonie.Models;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Phonie.Services;

public sealed class AirportFacilityService : IDisposable
{
    private const uint AirportDefinitionId = 0x02500001;
    private const int MaximumSavedReports = 10;

    private readonly object sync = new();
    private readonly Dictionary<uint, PendingAirportRequest> pendingRequests = new();
    private SimConnectClient? client;
    private string simulator = string.Empty;
    private uint nextRequestId = 0x02501000;
    private bool definitionReady;
    private bool disposed;

    public event EventHandler<AirportFacilityReport>? ReportCompleted;

    public event EventHandler<string>? LogMessage;

    public bool IsReady
    {
        get
        {
            lock (this.sync)
            {
                return this.client is { IsConnected: true } && this.definitionReady;
            }
        }
    }

    public void Attach(SimConnectClient connectedClient, string simulatorName)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(connectedClient);

        lock (this.sync)
        {
            this.DetachLocked();
            this.client = connectedClient;
            this.simulator = simulatorName;
            this.client.RawMessageReceived += this.Client_OnRawMessageReceived;

            try
            {
                this.BuildDefinition(connectedClient.Handle);
                this.definitionReady = true;
            }
            catch
            {
                this.DetachLocked();
                throw;
            }
        }

        this.PublishLog("Airport Data prêt : pistes, départs, fréquences, parkings et taxiways.");
    }

    public void Detach()
    {
        lock (this.sync)
        {
            this.DetachLocked();
        }
    }

    public bool RequestAirport(string icao)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var normalizedIcao = NormalizeIcao(icao);

        SimConnectClient connectedClient;
        uint requestId;
        PendingAirportRequest pending;

        lock (this.sync)
        {
            if (this.client is not { IsConnected: true } currentClient || !this.definitionReady)
            {
                return false;
            }

            var expiredRequestIds = this.pendingRequests
                .Where(pair => DateTimeOffset.UtcNow - pair.Value.CreatedAt > TimeSpan.FromSeconds(30))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var expiredRequestId in expiredRequestIds)
            {
                this.pendingRequests.Remove(expiredRequestId);
            }

            if (this.pendingRequests.Values.Any(item =>
                    string.Equals(item.Report.RequestedIcao, normalizedIcao, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            connectedClient = currentClient;
            requestId = unchecked(++this.nextRequestId);
            pending = new PendingAirportRequest(new AirportFacilityReport
            {
                Timestamp = DateTimeOffset.Now,
                Simulator = this.simulator,
                RequestedIcao = normalizedIcao,
            });
            this.pendingRequests.Add(requestId, pending);
        }

        var result = SimConnect_RequestFacilityData(
            connectedClient.Handle,
            AirportDefinitionId,
            requestId,
            normalizedIcao,
            string.Empty);

        if (result != 0)
        {
            lock (this.sync)
            {
                this.pendingRequests.Remove(requestId);
            }

            throw new InvalidOperationException($"SimConnect_RequestFacilityData {normalizedIcao} a échoué : 0x{result:X8}.");
        }

        this.PublishLog($"Airport Data : lecture de {normalizedIcao} demandée au simulateur.");
        return true;
    }

    private void BuildDefinition(IntPtr handle)
    {
        var fields = new[]
        {
            "OPEN AIRPORT",
            "LATITUDE", "LONGITUDE", "ALTITUDE", "MAGVAR", "NAME64", "ICAO", "REGION",
            "N_RUNWAYS", "N_STARTS", "N_FREQUENCIES", "N_TAXI_POINTS", "N_TAXI_PARKINGS", "N_TAXI_PATHS", "N_TAXI_NAMES",

            "OPEN RUNWAY",
            "LATITUDE", "LONGITUDE", "ALTITUDE", "HEADING", "LENGTH", "WIDTH", "PATTERN_ALTITUDE", "SLOPE", "SURFACE",
            "PRIMARY_NUMBER", "PRIMARY_DESIGNATOR", "SECONDARY_NUMBER", "SECONDARY_DESIGNATOR",
            "CLOSE RUNWAY",

            "OPEN START",
            "LATITUDE", "LONGITUDE", "ALTITUDE", "HEADING", "NUMBER", "DESIGNATOR", "TYPE",
            "CLOSE START",

            "OPEN FREQUENCY",
            "TYPE", "FREQUENCY", "NAME",
            "CLOSE FREQUENCY",

            "OPEN TAXI_PARKING",
            "TYPE", "TAXI_POINT_TYPE", "NAME", "SUFFIX", "NUMBER", "ORIENTATION", "HEADING", "RADIUS", "BIAS_X", "BIAS_Z",
            "CLOSE TAXI_PARKING",

            "OPEN TAXI_POINT",
            "TYPE", "ORIENTATION", "BIAS_X", "BIAS_Z",
            "CLOSE TAXI_POINT",

            "OPEN TAXI_PATH",
            "TYPE", "WIDTH", "LEFT_HALF_WIDTH", "RIGHT_HALF_WIDTH", "WEIGHT", "RUNWAY_NUMBER", "RUNWAY_DESIGNATOR",
            "LEFT_EDGE", "LEFT_EDGE_LIGHTED", "RIGHT_EDGE", "RIGHT_EDGE_LIGHTED", "CENTER_LINE", "CENTER_LINE_LIGHTED",
            "START", "END", "NAME_INDEX",
            "CLOSE TAXI_PATH",

            "OPEN TAXI_NAME",
            "NAME",
            "CLOSE TAXI_NAME",
            "CLOSE AIRPORT",
        };

        foreach (var field in fields)
        {
            var result = SimConnect_AddToFacilityDefinition(handle, AirportDefinitionId, field);
            if (result != 0)
            {
                throw new InvalidOperationException($"Définition Airport Data impossible sur '{field}' : 0x{result:X8}.");
            }
        }
    }

    private void Client_OnRawMessageReceived(object? sender, RawSimConnectMessageEventArgs eventArgs)
    {
        try
        {
            if (eventArgs.MessageId == SimConnectRecvId.FacilityData)
            {
                this.ProcessFacilityData(eventArgs.DataPointer, checked((int)eventArgs.DataSize));
            }
            else if (eventArgs.MessageId == SimConnectRecvId.FacilityDataEnd)
            {
                this.ProcessFacilityDataEnd(eventArgs.DataPointer, checked((int)eventArgs.DataSize));
            }
        }
        catch (Exception exception)
        {
            this.PublishLog($"Airport Data : paquet ignoré - {CleanMessage(exception)}");
        }
    }

    private void ProcessFacilityData(IntPtr dataPointer, int dataSize)
    {
        if (dataPointer == IntPtr.Zero || dataSize < FacilityPacketDecoder.HeaderSize)
        {
            this.PublishLog($"Airport Data : paquet Facilities trop court ({dataSize} octets).");
            return;
        }

        var buffer = new byte[dataSize];
        Marshal.Copy(dataPointer, buffer, 0, dataSize);

        FacilityPacketEnvelope envelope;
        try
        {
            envelope = FacilityPacketDecoder.DecodeEnvelope(buffer);
        }
        catch (Exception exception)
        {
            this.PublishLog($"Airport Data : en-tête Facilities invalide - {CleanMessage(exception)}");
            return;
        }

        var facilityType = (SimConnectFacilityDataType)envelope.FacilityType;
        PendingAirportRequest? pending;
        lock (this.sync)
        {
            this.pendingRequests.TryGetValue(envelope.UserRequestId, out pending);
        }

        if (pending is null)
        {
            return;
        }

        FacilityPacketDiagnostic diagnostic;
        lock (this.sync)
        {
            var sequence = ++pending.NextPacketSequence;
            diagnostic = new FacilityPacketDiagnostic
            {
                Sequence = sequence,
                Envelope = envelope,
                FacilityTypeName = facilityType.ToString(),
                HeaderHex = FacilityPacketDecoder.ToHex(buffer.AsSpan(0, FacilityPacketDecoder.HeaderSize)),
                PayloadHexPreview = FacilityPacketDecoder.ToHex(
                    buffer.AsSpan(FacilityPacketDecoder.HeaderSize),
                    maximumBytes: 96),
            };
            pending.Report.PacketDiagnostics.Add(diagnostic);
            pending.RawPackets.Add(new RawFacilityPacket(sequence, facilityType.ToString(), envelope.ItemIndex, buffer));
        }

        try
        {
            var reader = new PacketReader(buffer, FacilityPacketDecoder.HeaderSize);
            switch (facilityType)
            {
                case SimConnectFacilityDataType.Airport:
                    ParseAirport(reader, pending.Report);
                    break;
                case SimConnectFacilityDataType.Runway:
                    pending.Report.Runways.Add(ParseRunway(reader, envelope.ItemIndex));
                    break;
                case SimConnectFacilityDataType.Start:
                    pending.Report.Starts.Add(ParseStart(reader, envelope.ItemIndex));
                    break;
                case SimConnectFacilityDataType.Frequency:
                    pending.Report.Frequencies.Add(ParseFrequency(reader, envelope.ItemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiParking:
                    pending.Report.TaxiParkings.Add(ParseTaxiParking(reader, envelope.ItemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiPoint:
                    pending.Report.TaxiPoints.Add(ParseTaxiPoint(reader, envelope.ItemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiPath:
                    var path = FacilityPacketDecoder.DecodeTaxiPath(buffer, envelope, out var fields);
                    diagnostic.Fields.AddRange(fields);
                    pending.Report.TaxiPaths.Add(path);
                    break;
                case SimConnectFacilityDataType.TaxiName:
                    pending.Report.TaxiNames.Add(new AirportTaxiNameData(envelope.ItemIndex, reader.ReadAnsiString(32)));
                    break;
                default:
                    throw new InvalidDataException($"Type Facilities non prévu : {envelope.FacilityType}.");
            }

            diagnostic.ParseSucceeded = true;
        }
        catch (Exception exception)
        {
            diagnostic.ParseError = CleanMessage(exception);
            lock (this.sync)
            {
                pending.Report.ParseWarnings.Add(
                    $"{facilityType} #{envelope.ItemIndex} - paquet {diagnostic.Sequence} : {diagnostic.ParseError}");
            }
        }
    }

    private void ProcessFacilityDataEnd(IntPtr dataPointer, int dataSize)
    {
        if (dataPointer == IntPtr.Zero || dataSize < 16)
        {
            return;
        }

        var buffer = new byte[dataSize];
        Marshal.Copy(dataPointer, buffer, 0, dataSize);
        var requestId = BitConverter.ToUInt32(buffer, 12);
        PendingAirportRequest completedRequest;

        lock (this.sync)
        {
            if (!this.pendingRequests.Remove(requestId, out var removedRequest) || removedRequest is null)
            {
                return;
            }

            completedRequest = removedRequest;
            var sequence = ++completedRequest.NextPacketSequence;
            completedRequest.RawPackets.Add(new RawFacilityPacket(sequence, "FacilityDataEnd", 0, buffer));
        }

        _ = Task.Run(() => this.SaveAndPublishReport(completedRequest));
    }

    private void SaveAndPublishReport(PendingAirportRequest completedRequest)
    {
        var report = completedRequest.Report;
        try
        {
            ValidatePacketDiagnostics(report);
            ValidateReport(report);
            Directory.CreateDirectory(AppPaths.AirportDataDirectory);
            Directory.CreateDirectory(AppPaths.AirportDataRawDirectory);
            var safeSimulator = MakeSafeFileName(report.Simulator.Replace(" ", string.Empty, StringComparison.Ordinal));
            var safeIcao = MakeSafeFileName(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao);
            var stem = $"airport-{safeIcao}-{safeSimulator}-{report.Timestamp:yyyyMMdd-HHmmss}";
            var jsonPath = Path.Combine(AppPaths.AirportDataDirectory, stem + ".json");
            var textPath = Path.Combine(AppPaths.AirportDataDirectory, stem + ".txt");
            var diagnosticDirectory = Path.Combine(AppPaths.AirportDataRawDirectory, stem);

            report.JsonPath = jsonPath;
            report.TextPath = textPath;
            report.DiagnosticDirectoryPath = diagnosticDirectory;

            SaveDiagnosticArtifacts(report, completedRequest.RawPackets, diagnosticDirectory);

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
            File.WriteAllText(textPath, BuildTextReport(report), new UTF8Encoding(false));
            RotateReports();

            var diagnosticState = report.DiagnosticSummary.TaxiPathBinaryLayoutValidated
                ? "TaxiPath binaire cohérent"
                : "TaxiPath à analyser";
            this.PublishLog(
                $"Airport Data {safeIcao} : {report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s), " +
                $"{report.TaxiParkings.Count} parking(s), {report.TaxiPaths.Count} chemin(s) - {diagnosticState}. " +
                $"Diagnostic : logs\\airport-data\\raw\\{Path.GetFileName(diagnosticDirectory)}");
            this.ReportCompleted?.Invoke(this, report);
        }
        catch (Exception exception)
        {
            this.PublishLog($"Airport Data : sauvegarde impossible - {CleanMessage(exception)}");
        }
    }

    private static void ParseAirport(PacketReader reader, AirportFacilityReport report)
    {
        report.Latitude = reader.ReadDouble();
        report.Longitude = reader.ReadDouble();
        report.AltitudeMeters = reader.ReadDouble();
        report.MagneticVariationDegrees = reader.ReadSingle();
        report.Name = reader.ReadAnsiString(64);
        report.Icao = reader.ReadAnsiString(8).ToUpperInvariant();
        report.Region = reader.ReadAnsiString(8).ToUpperInvariant();
        report.RunwayCountDeclared = reader.ReadInt32();
        report.StartCountDeclared = reader.ReadInt32();
        report.FrequencyCountDeclared = reader.ReadInt32();
        report.TaxiPointCountDeclared = reader.ReadInt32();
        report.TaxiParkingCountDeclared = reader.ReadInt32();
        report.TaxiPathCountDeclared = reader.ReadInt32();
        report.TaxiNameCountDeclared = reader.ReadInt32();
    }

    private static AirportRunwayData ParseRunway(PacketReader reader, uint index) => new(
        index,
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32());

    private static AirportStartData ParseStart(PacketReader reader, uint index) => new(
        index,
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadDouble(),
        reader.ReadSingle(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32());

    private static AirportFrequencyData ParseFrequency(PacketReader reader, uint index)
    {
        var type = reader.ReadInt32();
        var frequencyHz = unchecked((uint)reader.ReadInt32());
        return new AirportFrequencyData(index, type, frequencyHz, frequencyHz / 1_000_000.0, reader.ReadAnsiString(64));
    }

    private static AirportTaxiParkingData ParseTaxiParking(PacketReader reader, uint index) => new(
        index,
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadUInt32(),
        reader.ReadInt32(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle(),
        reader.ReadSingle());

    private static AirportTaxiPointData ParseTaxiPoint(PacketReader reader, uint index) => new(
        index,
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadSingle(),
        reader.ReadSingle());

    private static void ValidatePacketDiagnostics(AirportFacilityReport report)
    {
        var taxiPathType = (int)SimConnectFacilityDataType.TaxiPath;
        var diagnostics = report.PacketDiagnostics.OrderBy(item => item.Sequence).ToArray();
        var taxiPathDiagnostics = diagnostics
            .Where(item => item.Envelope.FacilityType == taxiPathType)
            .ToArray();

        var summary = new FacilityDiagnosticSummary
        {
            PacketCount = diagnostics.Length,
            TaxiPathPacketCount = taxiPathDiagnostics.Length,
            ParsedTaxiPathPacketCount = taxiPathDiagnostics.Count(item => item.ParseSucceeded),
            FailedPacketCount = diagnostics.Count(item => !item.ParseSucceeded),
            SizeMismatchCount = diagnostics.Count(item => !item.Envelope.SizeMatches),
            UnexpectedPayloadLengthCount = taxiPathDiagnostics.Count(
                item => item.Envelope.PayloadLength != FacilityPacketDecoder.TaxiPathPayloadSize),
        };

        var firstFailedTaxiPath = taxiPathDiagnostics.FirstOrDefault(item => !item.ParseSucceeded);
        if (firstFailedTaxiPath is not null)
        {
            summary.FirstFailedTaxiPathSequence = firstFailedTaxiPath.Sequence;
            summary.FirstFailedTaxiPathIndex = firstFailedTaxiPath.Envelope.ItemIndex;
        }

        var pathsByIndex = report.TaxiPaths
            .GroupBy(item => item.Index)
            .ToDictionary(group => group.Key, group => group.First());
        foreach (var diagnostic in taxiPathDiagnostics.Where(item => item.ParseSucceeded))
        {
            if (!pathsByIndex.TryGetValue(diagnostic.Envelope.ItemIndex, out var path))
            {
                diagnostic.SemanticWarning = "TaxiPath décodé absent du rapport normalisé.";
            }
            else
            {
                diagnostic.SemanticWarning = BuildTaxiPathSemanticWarning(path);
            }
        }

        var suspiciousTaxiPaths = taxiPathDiagnostics
            .Where(item => !string.IsNullOrWhiteSpace(item.SemanticWarning))
            .ToArray();
        summary.SuspiciousTaxiPathCount = suspiciousTaxiPaths.Length;
        var firstSuspiciousTaxiPath = suspiciousTaxiPaths.FirstOrDefault();
        if (firstSuspiciousTaxiPath is not null)
        {
            summary.FirstSuspiciousTaxiPathSequence = firstSuspiciousTaxiPath.Sequence;
            summary.FirstSuspiciousTaxiPathIndex = firstSuspiciousTaxiPath.Envelope.ItemIndex;
        }

        foreach (var group in taxiPathDiagnostics
                     .Where(item => item.Envelope.ListSize > 0)
                     .GroupBy(item => item.Envelope.FacilityType))
        {
            var listSizes = group.Select(item => item.Envelope.ListSize).Distinct().ToArray();
            if (listSizes.Length != 1)
            {
                summary.InconsistentListSizeCount += listSizes.Length;
                continue;
            }

            var listSize = listSizes[0];
            var indexes = group.Select(item => item.Envelope.ItemIndex).ToArray();
            summary.DuplicateIndexCount += indexes
                .GroupBy(index => index)
                .Sum(indexGroup => Math.Max(0, indexGroup.Count() - 1));
            summary.OutOfRangeIndexCount += indexes.Count(index => index >= listSize);

            if (listSize <= 100_000)
            {
                var received = indexes.Where(index => index < listSize).ToHashSet();
                summary.MissingIndexCount += checked((int)listSize - received.Count);
            }
        }

        report.DiagnosticSummary = summary;

        if (summary.SizeMismatchCount > 0)
        {
            report.ParseWarnings.Add($"Facilities : {summary.SizeMismatchCount} paquet(s) avec taille déclarée différente de la taille reçue.");
        }

        if (summary.UnexpectedPayloadLengthCount > 0)
        {
            report.ParseWarnings.Add(
                $"TaxiPath : {summary.UnexpectedPayloadLengthCount} paquet(s) avec une charge utile différente de {FacilityPacketDecoder.TaxiPathPayloadSize} octets.");
        }

        if (summary.DuplicateIndexCount > 0 || summary.MissingIndexCount > 0 || summary.OutOfRangeIndexCount > 0)
        {
            report.ParseWarnings.Add(
                $"TaxiPath : index dupliqués {summary.DuplicateIndexCount}, manquants {summary.MissingIndexCount}, hors plage {summary.OutOfRangeIndexCount}.");
        }

        if (summary.InconsistentListSizeCount > 0)
        {
            report.ParseWarnings.Add("TaxiPath : ListSize incohérent dans la liste reçue.");
        }

        if (summary.FirstFailedTaxiPathSequence is not null)
        {
            report.ParseWarnings.Add(
                $"Premier TaxiPath non décodé : paquet {summary.FirstFailedTaxiPathSequence}, index {summary.FirstFailedTaxiPathIndex}.");
        }

        if (summary.FirstSuspiciousTaxiPathSequence is not null)
        {
            report.ParseWarnings.Add(
                $"Premier TaxiPath aux valeurs suspectes : paquet {summary.FirstSuspiciousTaxiPathSequence}, index {summary.FirstSuspiciousTaxiPathIndex}.");
        }
    }

    private static string BuildTaxiPathSemanticWarning(AirportTaxiPathData path)
    {
        var warnings = new List<string>();
        if (path.Type is < 0 or > 8)
        {
            warnings.Add($"TYPE={path.Type}");
        }

        if (!float.IsFinite(path.WidthMeters) || path.WidthMeters is < 0 or > 250)
        {
            warnings.Add($"WIDTH={path.WidthMeters:R}");
        }

        if (!float.IsFinite(path.LeftHalfWidthMeters) || path.LeftHalfWidthMeters is < 0 or > 250)
        {
            warnings.Add($"LEFT_HALF_WIDTH={path.LeftHalfWidthMeters:R}");
        }

        if (!float.IsFinite(path.RightHalfWidthMeters) || path.RightHalfWidthMeters is < 0 or > 250)
        {
            warnings.Add($"RIGHT_HALF_WIDTH={path.RightHalfWidthMeters:R}");
        }

        // RUNWAY_NUMBER et RUNWAY_DESIGNATOR ne sont sémantiquement définis
        // que pour un chemin de type RUNWAY. MSFS 2020 laisse parfois des octets
        // non initialisés dans ces champs pour TAXI/PARKING/PATH sans décaler le paquet.
        if (path.Type == 2 && path.RunwayNumber is < 1 or > 36)
        {
            warnings.Add($"RUNWAY_NUMBER={path.RunwayNumber}");
        }

        if (path.Type == 2 && path.RunwayDesignator is < 0 or > 7)
        {
            warnings.Add($"RUNWAY_DESIGNATOR={path.RunwayDesignator}");
        }

        if (path.StartIndex is < 0 or > 3999)
        {
            warnings.Add($"START={path.StartIndex}");
        }

        if (path.EndIndex is < 0 or > 3999)
        {
            warnings.Add($"END={path.EndIndex}");
        }

        return string.Join(", ", warnings);
    }

    private static void SaveDiagnosticArtifacts(
        AirportFacilityReport report,
        IReadOnlyList<RawFacilityPacket> rawPackets,
        string diagnosticDirectory)
    {
        Directory.CreateDirectory(diagnosticDirectory);
        var diagnosticBySequence = report.PacketDiagnostics.ToDictionary(item => item.Sequence);

        foreach (var rawPacket in rawPackets.OrderBy(item => item.Sequence))
        {
            var typeName = MakeSafeFileName(rawPacket.FacilityTypeName);
            var fileName = $"{rawPacket.Sequence:0000}-{typeName}-index-{rawPacket.ItemIndex}.bin";
            var path = Path.Combine(diagnosticDirectory, fileName);
            File.WriteAllBytes(path, rawPacket.Buffer);
            if (diagnosticBySequence.TryGetValue(rawPacket.Sequence, out var diagnostic))
            {
                diagnostic.RawFile = Path.Combine("logs", "airport-data", "raw", Path.GetFileName(diagnosticDirectory), fileName);
            }
        }

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        File.WriteAllText(
            Path.Combine(diagnosticDirectory, "diagnostic-summary.json"),
            JsonSerializer.Serialize(new
            {
                report.Timestamp,
                report.Simulator,
                report.RequestedIcao,
                report.Icao,
                report.DiagnosticSummary,
                report.ParseWarnings,
                Packets = report.PacketDiagnostics,
            }, jsonOptions),
            new UTF8Encoding(false));

        var packetCsv = new StringBuilder();
        packetCsv.AppendLine("sequence;type;declared_size;actual_size;version;message_id;request_id;unique_request_id;parent_request_id;is_list_item;item_index;list_size;payload_length;parse_ok;parse_error;semantic_warning;raw_file;header_hex;payload_hex_preview");
        foreach (var item in report.PacketDiagnostics.OrderBy(item => item.Sequence))
        {
            packetCsv.AppendLine(string.Join(";", new[]
            {
                item.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Csv(item.FacilityTypeName),
                item.Envelope.DeclaredSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.ActualSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.MessageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.UserRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.UniqueRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.ParentUniqueRequestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.IsListItem ? "1" : "0",
                item.Envelope.ItemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.ListSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.Envelope.PayloadLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
                item.ParseSucceeded ? "1" : "0",
                Csv(item.ParseError),
                Csv(item.SemanticWarning),
                Csv(item.RawFile),
                item.HeaderHex,
                item.PayloadHexPreview,
            }));
        }
        File.WriteAllText(Path.Combine(diagnosticDirectory, "packets.csv"), packetCsv.ToString(), new UTF8Encoding(false));

        var fieldsCsv = new StringBuilder();
        fieldsCsv.AppendLine("sequence;item_index;field;packet_offset;payload_offset;size;data_type;value;hex");
        foreach (var item in report.PacketDiagnostics
                     .Where(item => item.Envelope.FacilityType == (int)SimConnectFacilityDataType.TaxiPath)
                     .OrderBy(item => item.Sequence))
        {
            foreach (var field in item.Fields)
            {
                fieldsCsv.AppendLine(string.Join(";", new[]
                {
                    item.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    item.Envelope.ItemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Csv(field.Name),
                    field.PacketOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    field.PayloadOffset.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    field.Size.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    field.DataType,
                    Csv(field.Value),
                    field.Hex,
                }));
            }
        }
        File.WriteAllText(Path.Combine(diagnosticDirectory, "taxipath-fields.csv"), fieldsCsv.ToString(), new UTF8Encoding(false));

        var readme = new StringBuilder();
        readme.AppendLine("PHONIE DEV0.4.1.3 - SIA RADIO & CONTROLLER VOICES");
        readme.AppendLine();
        readme.AppendLine("Ce dossier contient la capture brute SimConnect Facilities de la demande aérodrome.");
        readme.AppendLine("Ne modifier aucun fichier avant transmission pour analyse.");
        readme.AppendLine();
        readme.AppendLine($"Aérodrome : {(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao)}");
        readme.AppendLine($"Simulateur : {report.Simulator}");
        readme.AppendLine($"Paquets : {report.DiagnosticSummary.PacketCount}");
        readme.AppendLine($"TaxiPaths : {report.DiagnosticSummary.ParsedTaxiPathPacketCount}/{report.DiagnosticSummary.TaxiPathPacketCount} décodés");
        readme.AppendLine($"TaxiPaths suspects : {report.DiagnosticSummary.SuspiciousTaxiPathCount}");
        readme.AppendLine($"Disposition binaire validée : {(report.DiagnosticSummary.TaxiPathBinaryLayoutValidated ? "OUI" : "NON")}");
        readme.AppendLine();
        readme.AppendLine("Fichiers :");
        readme.AppendLine("- diagnostic-summary.json : métadonnées, avertissements et traces de champs ;");
        readme.AppendLine("- packets.csv : index de tous les paquets ;");
        readme.AppendLine("- taxipath-fields.csv : valeur et octets exacts de chaque champ TaxiPath ;");
        readme.AppendLine("- *.bin : paquets SimConnect bruts, en-tête compris.");
        File.WriteAllText(Path.Combine(diagnosticDirectory, "LISEZ-MOI.txt"), readme.ToString(), new UTF8Encoding(false));
    }

    private static string Csv(string? value)
    {
        var normalized = (value ?? string.Empty).ReplaceLineEndings(" ");
        return normalized.Contains(';') || normalized.Contains('"')
            ? '"' + normalized.Replace("\"", "\"\"") + '"'
            : normalized;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safe = new string((value ?? string.Empty)
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();
        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static void ValidateReport(AirportFacilityReport report)
    {
        if (report.Runways.Count != report.RunwayCountDeclared)
        {
            report.ParseWarnings.Add($"Pistes reçues : {report.Runways.Count}, déclaré : {report.RunwayCountDeclared}.");
        }

        if (report.Frequencies.Count != report.FrequencyCountDeclared)
        {
            report.ParseWarnings.Add($"Fréquences reçues : {report.Frequencies.Count}, déclaré : {report.FrequencyCountDeclared}.");
        }

        foreach (var start in report.Starts.Where(item => item.Type == 1))
        {
            if (start.Number is < 1 or > 36 || start.Designator is < 0 or > 7)
            {
                report.ParseWarnings.Add($"Départ piste #{start.Index} incohérent : numéro {start.Number}, désignateur {start.Designator}.");
            }
        }

        foreach (var path in report.TaxiPaths)
        {
            if (path.Type is < 0 or > 8)
            {
                report.ParseWarnings.Add($"TaxiPath #{path.Index} : type {path.Type} hors plage.");
            }

            if (path.RunwayNumber is < 0 or > 45)
            {
                report.ParseWarnings.Add($"TaxiPath #{path.Index} : numéro de piste {path.RunwayNumber} hors plage.");
            }

            if (path.RunwayDesignator is < 0 or > 7)
            {
                report.ParseWarnings.Add($"TaxiPath #{path.Index} : désignateur {path.RunwayDesignator} hors plage.");
            }

            if (path.StartIndex is < 0 or > 3999 || path.EndIndex is < 0 or > 3999)
            {
                report.ParseWarnings.Add($"TaxiPath #{path.Index} : index {path.StartIndex}->{path.EndIndex} hors plage.");
            }
        }
    }

    private static string BuildTextReport(AirportFacilityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PHONIE DEV0.4.1.3 - SIA RADIO & CONTROLLER VOICES");
        builder.AppendLine($"Date : {report.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Simulateur : {report.Simulator}");
        builder.AppendLine($"Source : {report.Source}");
        builder.AppendLine($"Demande : {report.RequestedIcao}");
        builder.AppendLine($"Aérodrome : {report.Icao} - {report.Name} - région {report.Region}");
        builder.AppendLine($"Position : {report.Latitude:F6}, {report.Longitude:F6} - altitude {report.AltitudeMeters:F1} m - magvar {report.MagneticVariationDegrees:F1}°");
        builder.AppendLine();
        builder.AppendLine($"Pistes : {report.Runways.Count} / déclaré {report.RunwayCountDeclared}");
        foreach (var runway in report.Runways.OrderBy(item => item.Index))
        {
            builder.AppendLine(
                $"  #{runway.Index} - {FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator)} / " +
                $"{FormatRunwayEnd(runway.SecondaryNumber, runway.SecondaryDesignator)} - cap {runway.HeadingDegrees:F1}° - " +
                $"{runway.LengthMeters:F0} x {runway.WidthMeters:F0} m - surface {runway.Surface}");
        }

        builder.AppendLine();
        builder.AppendLine($"Départs : {report.Starts.Count} / déclaré {report.StartCountDeclared}");
        foreach (var start in report.Starts.OrderBy(item => item.Index))
        {
            if (start.Type == 1 && start.Number is >= 1 and <= 36 && start.Designator is >= 0 and <= 7)
            {
                builder.AppendLine($"  #{start.Index} - seuil piste {FormatRunwayEnd(start.Number, start.Designator)} - cap {start.HeadingDegrees:F1}°");
            }
            else
            {
                builder.AppendLine($"  #{start.Index} - départ non-piste conservé en donnée brute - type {start.Type} - numéro {start.Number} - désignateur {start.Designator}");
            }
        }

        builder.AppendLine();
        builder.AppendLine($"Fréquences : {report.Frequencies.Count} / déclaré {report.FrequencyCountDeclared}");
        foreach (var frequency in report.Frequencies.OrderBy(item => item.FrequencyHz))
        {
            builder.AppendLine($"  {frequency.FrequencyMhz:F3} MHz - type {frequency.Type} - {frequency.Name}");
        }

        builder.AppendLine();
        builder.AppendLine($"Parkings : {report.TaxiParkings.Count} / déclaré {report.TaxiParkingCountDeclared}");
        builder.AppendLine($"Points taxi : {report.TaxiPoints.Count} / déclaré {report.TaxiPointCountDeclared}");
        builder.AppendLine($"Chemins taxi : {report.TaxiPaths.Count} / déclaré {report.TaxiPathCountDeclared}");
        builder.AppendLine($"Noms taxi : {report.TaxiNames.Count} / déclaré {report.TaxiNameCountDeclared}");
        builder.AppendLine();
        builder.AppendLine("DIAGNOSTIC FACILITIES");
        builder.AppendLine($"  Paquets : {report.DiagnosticSummary.PacketCount}");
        builder.AppendLine($"  TaxiPaths décodés : {report.DiagnosticSummary.ParsedTaxiPathPacketCount} / {report.DiagnosticSummary.TaxiPathPacketCount}");
        builder.AppendLine($"  Valeurs TaxiPath suspectes : {report.DiagnosticSummary.SuspiciousTaxiPathCount}");
        builder.AppendLine($"  Tailles incohérentes : {report.DiagnosticSummary.SizeMismatchCount}");
        builder.AppendLine($"  Charges utiles TaxiPath inattendues : {report.DiagnosticSummary.UnexpectedPayloadLengthCount}");
        builder.AppendLine($"  Index dupliqués : {report.DiagnosticSummary.DuplicateIndexCount}");
        builder.AppendLine($"  Index manquants : {report.DiagnosticSummary.MissingIndexCount}");
        builder.AppendLine($"  Index hors plage : {report.DiagnosticSummary.OutOfRangeIndexCount}");
        builder.AppendLine($"  Disposition binaire TaxiPath validée : {(report.DiagnosticSummary.TaxiPathBinaryLayoutValidated ? "OUI" : "NON")}");
        builder.AppendLine($"  Capture brute : {report.DiagnosticDirectoryPath}");

        if (report.ParseWarnings.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("AVERTISSEMENTS DE LECTURE");
            foreach (var warning in report.ParseWarnings)
            {
                builder.AppendLine("  " + warning);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Les valeurs proviennent de la base du simulateur connecté. Aucune fréquence universelle n'est imposée par PHONIE.");
        return builder.ToString();
    }

    private static string FormatRunwayEnd(int number, int designator)
    {
        var suffix = designator switch
        {
            1 => "L",
            2 => "R",
            3 => "C",
            4 => "W",
            5 => "A",
            6 => "B",
            _ => string.Empty,
        };
        return number is >= 1 and <= 36 ? $"{number:00}{suffix}" : $"{number}{suffix}";
    }

    private static void RotateReports()
    {
        var files = new DirectoryInfo(AppPaths.AirportDataDirectory)
            .EnumerateFiles("airport-*.*", SearchOption.TopDirectoryOnly)
            .Where(file => string.Equals(file.Extension, ".json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(file.Extension, ".txt", StringComparison.OrdinalIgnoreCase))
            .GroupBy(file => Path.GetFileNameWithoutExtension(file.Name), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Max(file => file.LastWriteTimeUtc))
            .Skip(MaximumSavedReports)
            .SelectMany(group => group)
            .ToArray();

        foreach (var file in files)
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Rotation is best effort and must not break a completed report.
            }
        }

        if (!Directory.Exists(AppPaths.AirportDataRawDirectory))
        {
            return;
        }

        var diagnosticDirectories = new DirectoryInfo(AppPaths.AirportDataRawDirectory)
            .EnumerateDirectories("airport-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .Skip(MaximumSavedReports)
            .ToArray();
        foreach (var directory in diagnosticDirectories)
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch
            {
                // Rotation is best effort and must not break a completed report.
            }
        }
    }

    private static string NormalizeIcao(string icao)
    {
        var normalized = (icao ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length != 4 || normalized.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("Le code OACI doit contenir exactement quatre lettres ou chiffres.", nameof(icao));
        }

        return normalized;
    }

    private void DetachLocked()
    {
        if (this.client is not null)
        {
            this.client.RawMessageReceived -= this.Client_OnRawMessageReceived;
        }

        this.client = null;
        this.simulator = string.Empty;
        this.definitionReady = false;
        this.pendingRequests.Clear();
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private void PublishLog(string message) => this.LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Detach();
    }

    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int SimConnect_AddToFacilityDefinition(
        IntPtr hSimConnect,
        uint defineId,
        [MarshalAs(UnmanagedType.LPStr)] string fieldName);

    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int SimConnect_RequestFacilityData(
        IntPtr hSimConnect,
        uint defineId,
        uint requestId,
        [MarshalAs(UnmanagedType.LPStr)] string icao,
        [MarshalAs(UnmanagedType.LPStr)] string region);

    private sealed class PendingAirportRequest
    {
        public PendingAirportRequest(AirportFacilityReport report)
        {
            this.Report = report;
            this.CreatedAt = DateTimeOffset.UtcNow;
        }

        public AirportFacilityReport Report { get; }

        public DateTimeOffset CreatedAt { get; }

        public int NextPacketSequence { get; set; }

        public List<RawFacilityPacket> RawPackets { get; } = new();
    }

    private sealed record RawFacilityPacket(int Sequence, string FacilityTypeName, uint ItemIndex, byte[] Buffer);

    private sealed class PacketReader
    {
        private readonly byte[] buffer;
        private int offset;

        public PacketReader(byte[] buffer, int offset)
        {
            this.buffer = buffer;
            this.offset = offset;
        }

        public double ReadDouble()
        {
            this.EnsureAvailable(sizeof(double));
            var value = BitConverter.ToDouble(this.buffer, this.offset);
            this.offset += sizeof(double);
            return value;
        }

        public float ReadSingle()
        {
            this.EnsureAvailable(sizeof(float));
            var value = BitConverter.ToSingle(this.buffer, this.offset);
            this.offset += sizeof(float);
            return value;
        }

        public int ReadInt32()
        {
            this.EnsureAvailable(sizeof(int));
            var value = BitConverter.ToInt32(this.buffer, this.offset);
            this.offset += sizeof(int);
            return value;
        }

        public uint ReadUInt32()
        {
            this.EnsureAvailable(sizeof(uint));
            var value = BitConverter.ToUInt32(this.buffer, this.offset);
            this.offset += sizeof(uint);
            return value;
        }

        public string ReadAnsiString(int byteCount)
        {
            this.EnsureAvailable(byteCount);
            var end = Array.IndexOf(this.buffer, (byte)0, this.offset, byteCount);
            var length = end >= 0 ? end - this.offset : byteCount;
            var value = Encoding.ASCII.GetString(this.buffer, this.offset, length).Trim();
            this.offset += byteCount;
            return value;
        }

        private void EnsureAvailable(int byteCount)
        {
            if (this.offset < 0 || byteCount < 0 || this.offset + byteCount > this.buffer.Length)
            {
                throw new InvalidDataException($"Paquet incomplet : {byteCount} octets attendus à l'offset {this.offset}, taille {this.buffer.Length}.");
            }
        }
    }
}

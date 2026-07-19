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
    private const int FacilityDataHeaderSize = 40;
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
            "TYPE", "WIDTH", "WEIGHT", "RUNWAY_NUMBER", "RUNWAY_DESIGNATOR", "START", "END", "NAME_INDEX",
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
        if (dataPointer == IntPtr.Zero || dataSize < FacilityDataHeaderSize)
        {
            return;
        }

        var buffer = new byte[dataSize];
        Marshal.Copy(dataPointer, buffer, 0, dataSize);

        var requestId = BitConverter.ToUInt32(buffer, 12);
        var facilityType = (SimConnectFacilityDataType)BitConverter.ToInt32(buffer, 24);
        var itemIndex = BitConverter.ToUInt32(buffer, 32);

        PendingAirportRequest? pending;
        lock (this.sync)
        {
            this.pendingRequests.TryGetValue(requestId, out pending);
        }

        if (pending is null)
        {
            return;
        }

        try
        {
            var reader = new PacketReader(buffer, FacilityDataHeaderSize);
            switch (facilityType)
            {
                case SimConnectFacilityDataType.Airport:
                    ParseAirport(reader, pending.Report);
                    break;
                case SimConnectFacilityDataType.Runway:
                    pending.Report.Runways.Add(ParseRunway(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.Start:
                    pending.Report.Starts.Add(ParseStart(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.Frequency:
                    pending.Report.Frequencies.Add(ParseFrequency(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiParking:
                    pending.Report.TaxiParkings.Add(ParseTaxiParking(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiPoint:
                    pending.Report.TaxiPoints.Add(ParseTaxiPoint(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiPath:
                    pending.Report.TaxiPaths.Add(ParseTaxiPath(reader, itemIndex));
                    break;
                case SimConnectFacilityDataType.TaxiName:
                    pending.Report.TaxiNames.Add(new AirportTaxiNameData(itemIndex, reader.ReadAnsiString(32)));
                    break;
            }
        }
        catch (Exception exception)
        {
            lock (this.sync)
            {
                pending.Report.ParseWarnings.Add($"{facilityType} #{itemIndex} : {CleanMessage(exception)}");
            }
        }
    }

    private void ProcessFacilityDataEnd(IntPtr dataPointer, int dataSize)
    {
        if (dataPointer == IntPtr.Zero || dataSize < 16)
        {
            return;
        }

        var requestId = unchecked((uint)Marshal.ReadInt32(dataPointer, 12));
        PendingAirportRequest completedRequest;

        lock (this.sync)
        {
            if (!this.pendingRequests.Remove(requestId, out var removedRequest) || removedRequest is null)
            {
                return;
            }

            completedRequest = removedRequest;
        }

        _ = Task.Run(() => this.SaveAndPublishReport(completedRequest.Report));
    }

    private void SaveAndPublishReport(AirportFacilityReport report)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AirportDataDirectory);
            var safeSimulator = report.Simulator.Replace(" ", string.Empty, StringComparison.Ordinal);
            var safeIcao = string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao;
            var stem = $"airport-{safeIcao}-{safeSimulator}-{report.Timestamp:yyyyMMdd-HHmmss}";
            var jsonPath = Path.Combine(AppPaths.AirportDataDirectory, stem + ".json");
            var textPath = Path.Combine(AppPaths.AirportDataDirectory, stem + ".txt");

            report.JsonPath = jsonPath;
            report.TextPath = textPath;

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, jsonOptions), new UTF8Encoding(false));
            File.WriteAllText(textPath, BuildTextReport(report), new UTF8Encoding(false));
            RotateReports();

            this.PublishLog(
                $"Airport Data {safeIcao} : {report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s), " +
                $"{report.TaxiParkings.Count} parking(s), {report.TaxiPaths.Count} chemin(s). Rapport : logs\\airport-data\\{Path.GetFileName(textPath)}");
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

    private static AirportTaxiPathData ParseTaxiPath(PacketReader reader, uint index) => new(
        index,
        reader.ReadInt32(),
        reader.ReadSingle(),
        reader.ReadUInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadInt32(),
        reader.ReadUInt32());

    private static string BuildTextReport(AirportFacilityReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("PHONIE DEV0.2.5 - AIRPORT DATA");
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
            builder.AppendLine($"  #{start.Index} - piste {FormatRunwayEnd(start.Number, start.Designator)} - type {start.Type} - cap {start.HeadingDegrees:F1}°");
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
    }

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

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Phonie.Models;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Phonie.Services;

public sealed class NearbyAirportService : IDisposable
{
    private const uint RequestBaseId = 0x06101000;
    private const uint AirportListMessageId = 18;
    private const uint FacilityListTypeAirport = 0;
    private const int HeaderSize = 28;
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(8);

    private readonly object sync = new();
    private readonly Dictionary<uint, PendingAirportList> pending = new();
    private SimConnectClient? client;
    private uint nextRequestId = RequestBaseId;
    private DateTimeOffset lastRequest = DateTimeOffset.MinValue;
    private bool worldListFallbackActive;
    private bool worldListLoaded;
    private NearbyAirportSnapshot latest = new(
        DateTimeOffset.MinValue,
        Array.Empty<NearbyAirportData>(),
        "Liste des aérodromes en attente.");
    private bool disposed;

    public event EventHandler<NearbyAirportSnapshot>? AirportsUpdated;

    public event EventHandler<string>? LogMessage;

    public NearbyAirportSnapshot Latest
    {
        get
        {
            lock (this.sync)
            {
                return this.latest;
            }
        }
    }

    public void Attach(SimConnectClient connectedClient)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(connectedClient);

        lock (this.sync)
        {
            this.DetachLocked();
            this.client = connectedClient;
            this.client.RawMessageReceived += this.Client_OnRawMessageReceived;
            this.lastRequest = DateTimeOffset.MinValue;
            this.worldListFallbackActive = false;
            this.worldListLoaded = false;
        }

        this.PublishLog("Détection dynamique des aérodromes prête.");
    }

    public void Detach()
    {
        lock (this.sync)
        {
            this.DetachLocked();
        }
    }

    public bool RequestRefresh(bool force = false)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        SimConnectClient connectedClient;
        uint requestId;
        lock (this.sync)
        {
            if (this.client is not { IsConnected: true } current)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (this.worldListFallbackActive && this.worldListLoaded)
            {
                return false;
            }

            if (!force && now - this.lastRequest < RequestInterval)
            {
                return false;
            }

            foreach (var expired in this.pending
                         .Where(item => now - item.Value.CreatedAt > TimeSpan.FromSeconds(15))
                         .Select(item => item.Key)
                         .ToArray())
            {
                this.pending.Remove(expired);
            }

            if (force && this.pending.Count > 0)
            {
                this.pending.Clear();
            }

            if (this.pending.Count > 0)
            {
                return false;
            }

            connectedClient = current;
            requestId = unchecked(++this.nextRequestId);
            this.pending[requestId] = new PendingAirportList(now);
            this.lastRequest = now;
        }

        int result;
        var fallbackReason = string.Empty;
        try
        {
            result = SimConnect_RequestFacilitiesList_EX1(
                connectedClient.Handle,
                FacilityListTypeAirport,
                requestId);
            if (result != 0)
            {
                fallbackReason = $"retour 0x{result:X8}";
            }
        }
        catch (EntryPointNotFoundException)
        {
            result = -1;
            fallbackReason = "fonction absente";
        }

        if (result != 0)
        {
            lock (this.sync)
            {
                this.worldListFallbackActive = true;
            }

            this.PublishLog(
                $"Facilities EX1 indisponible ({fallbackReason}) : chargement unique de la liste mondiale des aérodromes.");
            try
            {
                result = SimConnect_RequestFacilitiesList(
                    connectedClient.Handle,
                    FacilityListTypeAirport,
                    requestId);
            }
            catch (EntryPointNotFoundException)
            {
                result = -1;
            }
        }

        if (result == 0)
        {
            return true;
        }

        lock (this.sync)
        {
            this.pending.Remove(requestId);
        }

        this.PublishLog($"Liste aérodromes refusée par SimConnect : 0x{result:X8}.");
        return false;
    }

    private void Client_OnRawMessageReceived(object? sender, RawSimConnectMessageEventArgs eventArgs)
    {
        if ((uint)eventArgs.MessageId != AirportListMessageId
            || eventArgs.DataPointer == IntPtr.Zero
            || eventArgs.DataSize < HeaderSize)
        {
            return;
        }

        try
        {
            var size = checked((int)eventArgs.DataSize);
            var buffer = new byte[size];
            Marshal.Copy(eventArgs.DataPointer, buffer, 0, size);
            this.ProcessAirportList(buffer);
        }
        catch (Exception exception)
        {
            this.PublishLog($"Liste aérodromes : paquet ignoré - {CleanMessage(exception)}");
        }
    }

    private void ProcessAirportList(byte[] buffer)
    {
        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));
        var arraySizeRaw = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16, 4));
        var arraySize = checked((int)arraySizeRaw);
        var entryNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(20, 4));
        var outOf = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(24, 4));

        PendingAirportList? request;
        lock (this.sync)
        {
            this.pending.TryGetValue(requestId, out request);
        }

        if (request is null)
        {
            return;
        }

        if (arraySize > 0)
        {
            var payloadSize = buffer.Length - HeaderSize;
            if (payloadSize <= 0 || payloadSize % arraySize != 0)
            {
                throw new InvalidDataException(
                    $"Disposition AirportList invalide : {payloadSize} octets pour {arraySize} élément(s).");
            }

            var stride = payloadSize / arraySize;
            for (var index = 0; index < arraySize; index++)
            {
                var offset = HeaderSize + (index * stride);
                var airport = DecodeAirport(buffer.AsSpan(offset, stride));
                if (airport is null)
                {
                    continue;
                }

                request.Airports[airport.Icao] = airport;
            }
        }

        NearbyAirportSnapshot? completed = null;
        lock (this.sync)
        {
            request.ReceivedPackets.Add(entryNumber);
            var expectedPackets = checked((int)outOf);
            if (outOf == 0 || request.ReceivedPackets.Count >= expectedPackets || entryNumber + 1 >= outOf)
            {
                this.pending.Remove(requestId);
                var airports = request.Airports.Values
                    .OrderBy(item => item.Icao, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                completed = new NearbyAirportSnapshot(
                    DateTimeOffset.UtcNow,
                    airports,
                    $"{airports.Length} aérodrome(s) dans le cache Facilities proche.");
                this.latest = completed;
                if (this.worldListFallbackActive)
                {
                    this.worldListLoaded = true;
                }
            }
        }

        if (completed is not null)
        {
            this.AirportsUpdated?.Invoke(this, completed);
        }
    }

    private static NearbyAirportData? DecodeAirport(ReadOnlySpan<byte> data)
    {
        // MSFS 2020 : ident[6], region[3], doubles aux offsets 9/17/25 (33 octets).
        // MSFS 2024 : ident[9], region[3], doubles aux offsets 12/20/28, avec éventuel padding final.
        var modern = data.Length >= 36;
        var identLength = modern ? 9 : 6;
        var regionOffset = identLength;
        var latitudeOffset = modern ? 12 : 9;
        if (data.Length < latitudeOffset + 24)
        {
            return null;
        }

        var icao = ReadAnsiString(data.Slice(0, identLength));
        if (icao.Length != 4 || icao.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            return null;
        }

        var region = ReadAnsiString(data.Slice(regionOffset, 3));
        var latitude = BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(data.Slice(latitudeOffset, 8)));
        var longitude = BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(data.Slice(latitudeOffset + 8, 8)));
        var altitude = BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(data.Slice(latitudeOffset + 16, 8)));
        if (!double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return null;
        }

        return new NearbyAirportData(
            icao.ToUpperInvariant(),
            region.ToUpperInvariant(),
            latitude,
            longitude,
            altitude);
    }

    private static string ReadAnsiString(ReadOnlySpan<byte> data)
    {
        var zero = data.IndexOf((byte)0);
        var slice = zero >= 0 ? data[..zero] : data;
        return Encoding.ASCII.GetString(slice).Trim();
    }

    private void DetachLocked()
    {
        if (this.client is not null)
        {
            this.client.RawMessageReceived -= this.Client_OnRawMessageReceived;
        }

        this.client = null;
        this.pending.Clear();
        this.latest = new NearbyAirportSnapshot(
            DateTimeOffset.MinValue,
            Array.Empty<NearbyAirportData>(),
            "Liste des aérodromes en attente.");
        this.lastRequest = DateTimeOffset.MinValue;
        this.worldListFallbackActive = false;
        this.worldListLoaded = false;
    }

    private void PublishLog(string message) => this.LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    [DllImport("SimConnect.dll", ExactSpelling = true)]
    private static extern int SimConnect_RequestFacilitiesList_EX1(
        IntPtr hSimConnect,
        uint facilityType,
        uint requestId);

    [DllImport("SimConnect.dll", ExactSpelling = true)]
    private static extern int SimConnect_RequestFacilitiesList(
        IntPtr hSimConnect,
        uint facilityType,
        uint requestId);

    private sealed class PendingAirportList(DateTimeOffset createdAt)
    {
        public DateTimeOffset CreatedAt { get; } = createdAt;

        public Dictionary<string, NearbyAirportData> Airports { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public HashSet<uint> ReceivedPackets { get; } = new();
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Detach();
    }
}

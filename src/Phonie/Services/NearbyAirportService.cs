using System.Runtime.InteropServices;
using Phonie.Core;
using Phonie.Models;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Phonie.Services;

public sealed class NearbyAirportService : IDisposable
{
    private const uint RequestBaseId = 0x06101000;
    private const uint AirportListMessageId = 18;
    private const uint FacilityListTypeAirport = 0;
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(8);

    private readonly object sync = new();
    private readonly Dictionary<uint, PendingAirportList> pending = new();
    private SimConnectClient? client;
    private uint nextRequestId = RequestBaseId;
    private DateTimeOffset lastRequest = DateTimeOffset.MinValue;
    private bool worldListFallbackActive;
    private bool worldListLoaded;
    private bool compatibilityLayoutLogged;
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
            || eventArgs.DataSize < AirportListPacketDecoder.HeaderSize)
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
        var packet = AirportListPacketDecoder.Decode(buffer);

        PendingAirportList? request;
        lock (this.sync)
        {
            this.pending.TryGetValue(packet.RequestId, out request);
        }

        if (request is null)
        {
            return;
        }

        foreach (var airport in packet.Airports)
        {
            request.Airports[airport.Icao] = new NearbyAirportData(
                airport.Icao,
                airport.Region,
                airport.Latitude,
                airport.Longitude,
                airport.AltitudeMeters);
        }

        if (packet.CompatibilitySlotCount > 0 && !this.compatibilityLayoutLogged)
        {
            this.compatibilityLayoutLogged = true;
            this.PublishLog(
                $"Liste aérodromes : disposition MSFS compatible détectée " +
                $"({packet.EntryStride} octets, {packet.CompatibilitySlotCount} emplacement supplémentaire ignoré).");
        }

        NearbyAirportSnapshot? completed = null;
        lock (this.sync)
        {
            request.ReceivedPackets.Add(packet.EntryNumber);
            var expectedPackets = checked((int)packet.OutOf);
            if (packet.OutOf == 0
                || request.ReceivedPackets.Count >= expectedPackets
                || packet.EntryNumber + 1 >= packet.OutOf)
            {
                this.pending.Remove(packet.RequestId);
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
        this.compatibilityLayoutLogged = false;
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

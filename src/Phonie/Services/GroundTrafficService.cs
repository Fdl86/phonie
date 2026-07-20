using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Phonie.Models;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace Phonie.Services;

public sealed class GroundTrafficService : IDisposable
{
    private const uint DefinitionId = 0x04700001;
    private const uint RequestBaseId = 0x04701000;
    private const uint SimObjectDataByTypeMessageId = 9;
    private const int DataTypeInt32 = 1;
    private const int DataTypeFloat64 = 4;
    private const int DataTypeString32 = 6;
    private const int SimObjectTypeAircraft = 2;
    private const uint RadiusMeters = 6_000;
    private readonly object sync = new();
    private readonly Dictionary<uint, PendingTrafficRequest> pending = new();
    private SimConnectClient? client;
    private uint nextRequestId = RequestBaseId;
    private bool definitionReady;
    private bool disposed;

    public event EventHandler<GroundTrafficSnapshot>? SnapshotReceived;

    public event EventHandler<string>? LogMessage;

    public void Attach(SimConnectClient connectedClient)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(connectedClient);

        lock (this.sync)
        {
            this.DetachLocked();
            this.client = connectedClient;
            this.client.RawMessageReceived += this.Client_OnRawMessageReceived;

            try
            {
                AddDefinition(connectedClient.Handle, "PLANE LATITUDE", "degrees", DataTypeFloat64);
                AddDefinition(connectedClient.Handle, "PLANE LONGITUDE", "degrees", DataTypeFloat64);
                AddDefinition(connectedClient.Handle, "GROUND VELOCITY", "knots", DataTypeFloat64);
                AddDefinition(connectedClient.Handle, "SIM ON GROUND", "bool", DataTypeInt32);
                AddDefinition(connectedClient.Handle, "ATC ID", null, DataTypeString32);
                this.definitionReady = true;
            }
            catch
            {
                this.DetachLocked();
                throw;
            }
        }

        this.PublishLog("Trafic sol SimConnect prêt : interrogation événementielle des avions proches.");
    }

    public void Detach()
    {
        lock (this.sync)
        {
            this.DetachLocked();
        }
    }

    public bool RequestSnapshot()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        SimConnectClient connectedClient;
        uint requestId;
        lock (this.sync)
        {
            if (!this.definitionReady || this.client is not { IsConnected: true } current)
            {
                return false;
            }

            connectedClient = current;
            requestId = unchecked(++this.nextRequestId);
            this.pending[requestId] = new PendingTrafficRequest(DateTimeOffset.UtcNow);
            foreach (var expired in this.pending
                         .Where(item => DateTimeOffset.UtcNow - item.Value.CreatedAt > TimeSpan.FromSeconds(8))
                         .Select(item => item.Key)
                         .ToArray())
            {
                this.pending.Remove(expired);
            }
        }

        var result = SimConnect_RequestDataOnSimObjectType(
            connectedClient.Handle,
            requestId,
            DefinitionId,
            RadiusMeters,
            SimObjectTypeAircraft);
        if (result != 0)
        {
            lock (this.sync)
            {
                this.pending.Remove(requestId);
            }

            this.SnapshotReceived?.Invoke(this, new GroundTrafficSnapshot(
                DateTimeOffset.UtcNow,
                false,
                Array.Empty<GroundTrafficContactData>(),
                $"Demande trafic refusée : 0x{result:X8}."));
            return false;
        }

        return true;
    }

    private void Client_OnRawMessageReceived(object? sender, RawSimConnectMessageEventArgs eventArgs)
    {
        if ((uint)eventArgs.MessageId != SimObjectDataByTypeMessageId
            || eventArgs.DataPointer == IntPtr.Zero
            || eventArgs.DataSize < 40)
        {
            return;
        }

        try
        {
            var size = checked((int)eventArgs.DataSize);
            var buffer = new byte[size];
            Marshal.Copy(eventArgs.DataPointer, buffer, 0, size);
            var requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12, 4));
            var objectId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(16, 4));
            var entryNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(28, 4));
            var outOf = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(32, 4));

            PendingTrafficRequest? request;
            lock (this.sync)
            {
                this.pending.TryGetValue(requestId, out request);
            }

            if (request is null)
            {
                return;
            }

            if (buffer.Length < 100)
            {
                throw new InvalidDataException($"Paquet trafic trop court : {buffer.Length} octets.");
            }

            var latitude = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(40, 8)));
            var longitude = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(48, 8)));
            var groundSpeed = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(56, 8)));
            var onGround = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(64, 4)) != 0;
            var callsign = ReadAnsiString(buffer.AsSpan(68, 32));

            GroundTrafficSnapshot? completed = null;
            lock (this.sync)
            {
                request.Contacts.Add(new GroundTrafficContactData(
                    objectId,
                    callsign,
                    latitude,
                    longitude,
                    groundSpeed,
                    onGround,
                    DateTimeOffset.UtcNow));

                if (outOf > 0 && entryNumber + 1 >= outOf)
                {
                    this.pending.Remove(requestId);
                    completed = new GroundTrafficSnapshot(
                        DateTimeOffset.UtcNow,
                        true,
                        request.Contacts.ToArray(),
                        $"{request.Contacts.Count} objet(s) avion reçu(s).");
                }
            }

            if (completed is not null)
            {
                this.SnapshotReceived?.Invoke(this, completed);
            }
        }
        catch (Exception exception)
        {
            this.PublishLog($"Trafic sol : paquet ignoré - {CleanMessage(exception)}");
        }
    }

    private static string ReadAnsiString(ReadOnlySpan<byte> data)
    {
        var zero = data.IndexOf((byte)0);
        var slice = zero >= 0 ? data[..zero] : data;
        return Encoding.ASCII.GetString(slice).Trim().ToUpperInvariant();
    }

    private static void AddDefinition(IntPtr handle, string name, string? unit, int dataType)
    {
        var result = SimConnect_AddToDataDefinition(
            handle,
            DefinitionId,
            name,
            unit,
            dataType,
            0,
            uint.MaxValue);
        if (result != 0)
        {
            throw new InvalidOperationException($"Définition trafic '{name}' impossible : 0x{result:X8}.");
        }
    }

    private void DetachLocked()
    {
        if (this.client is not null)
        {
            this.client.RawMessageReceived -= this.Client_OnRawMessageReceived;
        }

        this.client = null;
        this.definitionReady = false;
        this.pending.Clear();
    }

    private void PublishLog(string message) => this.LogMessage?.Invoke(this, message);

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    [DllImport("SimConnect.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern int SimConnect_AddToDataDefinition(
        IntPtr hSimConnect,
        uint defineId,
        string datumName,
        string? unitsName,
        int datumType,
        float epsilon,
        uint datumId);

    [DllImport("SimConnect.dll", ExactSpelling = true)]
    private static extern int SimConnect_RequestDataOnSimObjectType(
        IntPtr hSimConnect,
        uint requestId,
        uint defineId,
        uint radiusMeters,
        int objectType);

    private sealed class PendingTrafficRequest(DateTimeOffset createdAt)
    {
        public DateTimeOffset CreatedAt { get; } = createdAt;

        public List<GroundTrafficContactData> Contacts { get; } = new();
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

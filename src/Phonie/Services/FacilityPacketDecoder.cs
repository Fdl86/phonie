using System.Buffers.Binary;
using System.Globalization;
using Phonie.Models;

namespace Phonie.Services;

public static class FacilityPacketDecoder
{
    public const int HeaderSize = 40;
    public const int TaxiPathPayloadSize = 64;

    public static FacilityPacketEnvelope DecodeEnvelope(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderSize)
        {
            throw new InvalidDataException($"En-tête Facilities incomplet : {packet.Length} octets reçus, {HeaderSize} attendus.");
        }

        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(0, 4));
        if (declaredSize < HeaderSize)
        {
            throw new InvalidDataException($"Taille Facilities déclarée invalide : {declaredSize} octets.");
        }

        var actualPayloadLength = packet.Length - HeaderSize;
        return new FacilityPacketEnvelope(
            declaredSize,
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(8, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(12, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(16, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(20, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(24, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(28, 4)) != 0,
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(32, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(36, 4)),
            packet.Length,
            actualPayloadLength);
    }

    public static AirportTaxiPathData DecodeTaxiPath(
        ReadOnlySpan<byte> packet,
        FacilityPacketEnvelope envelope,
        out List<FacilityFieldDiagnostic> fields)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (packet.Length < HeaderSize + TaxiPathPayloadSize)
        {
            throw new InvalidDataException(
                $"TaxiPath incomplet : {packet.Length - HeaderSize} octets de charge utile, {TaxiPathPayloadSize} attendus.");
        }

        var payload = packet.Slice(HeaderSize, TaxiPathPayloadSize);
        var reader = new DiagnosticPayloadReader(payload, HeaderSize);

        var type = reader.ReadInt32("TYPE");
        var width = reader.ReadSingle("WIDTH");
        var leftHalfWidth = reader.ReadSingle("LEFT_HALF_WIDTH");
        var rightHalfWidth = reader.ReadSingle("RIGHT_HALF_WIDTH");
        var weight = reader.ReadUInt32("WEIGHT");
        var runwayNumber = reader.ReadInt32("RUNWAY_NUMBER");
        var runwayDesignator = reader.ReadInt32("RUNWAY_DESIGNATOR");
        var leftEdge = reader.ReadInt32("LEFT_EDGE");
        var leftEdgeLighted = reader.ReadInt32("LEFT_EDGE_LIGHTED") != 0;
        var rightEdge = reader.ReadInt32("RIGHT_EDGE");
        var rightEdgeLighted = reader.ReadInt32("RIGHT_EDGE_LIGHTED") != 0;
        var centerLine = reader.ReadInt32("CENTER_LINE") != 0;
        var centerLineLighted = reader.ReadInt32("CENTER_LINE_LIGHTED") != 0;
        var start = reader.ReadInt32("START");
        var end = reader.ReadInt32("END");
        var nameIndex = reader.ReadUInt32("NAME_INDEX");
        fields = reader.Fields;

        return new AirportTaxiPathData(
            envelope.ItemIndex,
            type,
            width,
            leftHalfWidth,
            rightHalfWidth,
            weight,
            runwayNumber,
            runwayDesignator,
            leftEdge,
            leftEdgeLighted,
            rightEdge,
            rightEdgeLighted,
            centerLine,
            centerLineLighted,
            start,
            end,
            nameIndex);
    }

    public static string ToHex(ReadOnlySpan<byte> data, int maximumBytes = int.MaxValue)
    {
        var length = Math.Min(data.Length, Math.Max(0, maximumBytes));
        if (length == 0)
        {
            return string.Empty;
        }

        return Convert.ToHexString(data.Slice(0, length)).ToLowerInvariant();
    }

    private sealed class DiagnosticPayloadReader
    {
        private readonly byte[] payload;
        private readonly int packetBaseOffset;
        private int offset;

        public DiagnosticPayloadReader(ReadOnlySpan<byte> payload, int packetBaseOffset)
        {
            this.payload = payload.ToArray();
            this.packetBaseOffset = packetBaseOffset;
        }

        public List<FacilityFieldDiagnostic> Fields { get; } = new();

        public int ReadInt32(string name)
        {
            this.EnsureAvailable(4);
            var fieldOffset = this.offset;
            var value = BinaryPrimitives.ReadInt32LittleEndian(this.payload.AsSpan(this.offset, 4));
            this.AddField(name, fieldOffset, 4, "INT32", value.ToString(CultureInfo.InvariantCulture));
            this.offset += 4;
            return value;
        }

        public uint ReadUInt32(string name)
        {
            this.EnsureAvailable(4);
            var fieldOffset = this.offset;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(this.payload.AsSpan(this.offset, 4));
            this.AddField(name, fieldOffset, 4, "UINT32", value.ToString(CultureInfo.InvariantCulture));
            this.offset += 4;
            return value;
        }

        public float ReadSingle(string name)
        {
            this.EnsureAvailable(4);
            var fieldOffset = this.offset;
            var bits = BinaryPrimitives.ReadInt32LittleEndian(this.payload.AsSpan(this.offset, 4));
            var value = BitConverter.Int32BitsToSingle(bits);
            this.AddField(name, fieldOffset, 4, "FLOAT32", value.ToString("R", CultureInfo.InvariantCulture));
            this.offset += 4;
            return value;
        }

        private void AddField(string name, int payloadOffset, int size, string dataType, string value)
        {
            this.Fields.Add(new FacilityFieldDiagnostic(
                name,
                this.packetBaseOffset + payloadOffset,
                payloadOffset,
                size,
                dataType,
                value,
                FacilityPacketDecoder.ToHex(this.payload.AsSpan(payloadOffset, size))));
        }

        private void EnsureAvailable(int byteCount)
        {
            if (this.offset < 0 || this.offset + byteCount > this.payload.Length)
            {
                throw new InvalidDataException(
                    $"Charge utile TaxiPath incomplète à l'offset {this.offset} : {byteCount} octets attendus, taille {this.payload.Length}.");
            }
        }
    }
}

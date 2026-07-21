using System.Buffers.Binary;
using System.Text;

namespace Phonie.Core;

public sealed record AirportListEntry(
    string Icao,
    string Region,
    double Latitude,
    double Longitude,
    double AltitudeMeters);

public sealed record AirportListPacket(
    uint RequestId,
    uint DeclaredArraySize,
    uint EntryNumber,
    uint OutOf,
    int EntryStride,
    int DecodedSlotCount,
    int CompatibilitySlotCount,
    IReadOnlyList<AirportListEntry> Airports);

/// <summary>
/// Décode les paquets natifs SIMCONNECT_RECV_AIRPORT_LIST sans dépendre du wrapper .NET.
/// Le callback brut de SimConnect.NET 0.2.1 peut exposer un emplacement rgData de
/// compatibilité supplémentaire. Le décodeur accepte donc exactement dwArraySize ou
/// dwArraySize + 1 emplacements, puis ignore tout emplacement vide/non valide.
/// </summary>
public static class AirportListPacketDecoder
{
    public const int HeaderSize = 28;
    public const int Msfs2024EntrySize = 36;
    public const int Msfs2020PackedEntrySize = 33;

    public static AirportListPacket Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"Paquet AirportList trop court : {buffer.Length} octets, minimum {HeaderSize}.");
        }

        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4));
        var declaredArraySize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));
        var entryNumber = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(20, 4));
        var outOf = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(24, 4));
        var payload = buffer[HeaderSize..];

        var layouts = new[]
        {
            TryDecodeLayout(payload, declaredArraySize, Msfs2024EntrySize, modern: true),
            TryDecodeLayout(payload, declaredArraySize, Msfs2020PackedEntrySize, modern: false),
        }
        .Where(item => item is not null)
        .Cast<DecodedLayout>()
        .OrderByDescending(item => item.Score)
        .ThenByDescending(item => item.EntryStride)
        .ToArray();

        if (layouts.Length == 0)
        {
            throw new InvalidDataException(
                $"Disposition AirportList inconnue : {payload.Length} octets pour " +
                $"{declaredArraySize} élément(s). Attendu : {Msfs2024EntrySize} ou " +
                $"{Msfs2020PackedEntrySize} octets par entrée, avec au plus un emplacement de compatibilité.");
        }

        var selected = layouts[0];
        return new AirportListPacket(
            requestId,
            declaredArraySize,
            entryNumber,
            outOf,
            selected.EntryStride,
            selected.SlotCount,
            selected.CompatibilitySlotCount,
            selected.Airports);
    }

    private static DecodedLayout? TryDecodeLayout(
        ReadOnlySpan<byte> payload,
        uint declaredArraySize,
        int stride,
        bool modern)
    {
        if (payload.Length % stride != 0)
        {
            return null;
        }

        var slotCount = payload.Length / stride;
        var declared = checked((int)declaredArraySize);
        if (slotCount != declared && slotCount != declared + 1)
        {
            return null;
        }

        var airports = new Dictionary<string, AirportListEntry>(StringComparer.OrdinalIgnoreCase);
        // dwArraySize reste la source de vérité. Lorsque le wrapper expose un
        // emplacement rgData supplémentaire, celui-ci est une capacité de
        // compatibilité en fin de paquet et ne fait pas partie de la liste.
        var slotsToDecode = Math.Min(slotCount, declared);
        for (var index = 0; index < slotsToDecode; index++)
        {
            var entry = DecodeAirport(payload.Slice(index * stride, stride), modern);
            if (entry is not null)
            {
                airports[entry.Icao] = entry;
            }
        }

        if (declared > 0 && airports.Count == 0)
        {
            return null;
        }

        var compatibilitySlots = Math.Max(0, slotCount - declared);
        var exactLayoutBonus = compatibilitySlots == 0 ? 10_000 : 9_000;
        var formatBonus = modern ? 500 : 0;
        var score = exactLayoutBonus + formatBonus + airports.Count;
        return new DecodedLayout(
            stride,
            slotCount,
            compatibilitySlots,
            airports.Values.OrderBy(item => item.Icao, StringComparer.OrdinalIgnoreCase).ToArray(),
            score);
    }

    private static AirportListEntry? DecodeAirport(ReadOnlySpan<byte> data, bool modern)
    {
        var identLength = modern ? 9 : 6;
        var regionOffset = identLength;
        var latitudeOffset = modern ? 12 : 9;
        if (data.Length < latitudeOffset + 24)
        {
            return null;
        }

        var icao = ReadAnsiString(data.Slice(0, identLength)).ToUpperInvariant();
        if (icao.Length != 4 || icao.Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            return null;
        }

        var region = ReadAnsiString(data.Slice(regionOffset, 3)).ToUpperInvariant();
        var latitude = ReadDouble(data, latitudeOffset);
        var longitude = ReadDouble(data, latitudeOffset + 8);
        var altitude = ReadDouble(data, latitudeOffset + 16);
        if (!double.IsFinite(latitude)
            || !double.IsFinite(longitude)
            || !double.IsFinite(altitude)
            || latitude is < -90 or > 90
            || longitude is < -180 or > 180)
        {
            return null;
        }

        return new AirportListEntry(icao, region, latitude, longitude, altitude);
    }

    private static double ReadDouble(ReadOnlySpan<byte> data, int offset) =>
        BitConverter.Int64BitsToDouble(
            BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8)));

    private static string ReadAnsiString(ReadOnlySpan<byte> data)
    {
        var zero = data.IndexOf((byte)0);
        var slice = zero >= 0 ? data[..zero] : data;
        return Encoding.ASCII.GetString(slice).Trim();
    }

    private sealed record DecodedLayout(
        int EntryStride,
        int SlotCount,
        int CompatibilitySlotCount,
        IReadOnlyList<AirportListEntry> Airports,
        int Score);
}

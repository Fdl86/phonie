using System.Buffers.Binary;
using Phonie.Services;

var failures = new List<string>();

var turboProfile = Phonie.Models.SpeechRecognitionProfiles.Get(
    Phonie.Models.SpeechRecognitionProfile.WhisperLargeV3TurboVulkan);
if (!turboProfile.UsesVulkan
    || turboProfile.Backend != Phonie.Models.SpeechRecognitionBackend.Whisper
    || !turboProfile.DisplayName.Contains("Large-v3 Turbo", StringComparison.Ordinal))
{
    failures.Add("Profil Whisper Large-v3 Turbo Vulkan absent ou mal configuré.");
}

if (!Enum.IsDefined(typeof(Phonie.Models.SpeechModelState), Phonie.Models.SpeechModelState.WarmingUp))
{
    failures.Add("État de préchauffage Turbo absent.");
}

var vulkanProfiles = Phonie.Models.SpeechRecognitionProfiles.All
    .Where(profile => profile.UsesVulkan)
    .Select(profile => profile.Profile)
    .ToArray();
if (!vulkanProfiles.Contains(Phonie.Models.SpeechRecognitionProfile.WhisperSmallVulkan)
    || !vulkanProfiles.Contains(Phonie.Models.SpeechRecognitionProfile.WhisperLargeV3TurboVulkan))
{
    failures.Add("Profils Vulkan nécessaires au benchmark GPU absents.");
}

Check(
    "Phrase laboratoire exacte",
    "Poitiers Tour, Fox Hôtel Novembre Novembre Yankee, au parking pour tours de piste.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Variante laboratoire roulage",
    "Poitiers Tour, Fox Hotel novembre novembre yankee, au parking aviation générale, demande roulage.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking aviation générale",
    expectedIntention: "roulage");

Check(
    "Transcription Whisper réelle",
    "Poitiers tour, Fox Hotel, Novembre, Bonne-Novembre, et on quibonjour. Au parking, galesson général pour des tours de pisse avec l'information Alpha.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste",
    expectedAtis: "A");

Check(
    "Transcription Vosk réelle",
    "poitiers tour de fox hôtel novembre deux qui bonjour au parking son général pour des tours de piste l'information alpes",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste",
    expectedAtis: "A");

Check(
    "Indicatif précédent F-GABC",
    "Poitiers tour, Fox-Colf, Alfa Bravo, Charlie au parking, demande des tours de pistes.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Immatriculation écrite avec tiret",
    "Poitiers Tour, F-HNNY, au parking pour tours de piste.",
    "F-HNNY",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-HNNY",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Mots fusionnés FoxGolf",
    "Poitiers Tour, FoxGolf Alfa Bravo Charlie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Transcription Fabre Beauchardie",
    "Poitiers Tour, Fox Gold Fabre Beauchardie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

Check(
    "Transcription va pas",
    "Poitiers Tour, Fox Gold va pas Bravo Charlie, au parking pour tours de piste.",
    "F-GABC",
    expectedStation: "Poitiers Tour",
    expectedCallsign: "F-GABC",
    expectedPosition: "parking",
    expectedIntention: "tours de piste");

RunFacilityDecoderTests();

var stationOnly = PhraseologyService.Analyze("Poitiers Tour, bonjour.", "F-HNNY");
if (stationOnly.Callsign is not null)
{
    failures.Add($"Station seule : aucun indicatif attendu, obtenu {stationOnly.Callsign}.");
}

var wrongAircraft = PhraseologyService.Analyze(
    "Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking pour tours de piste.",
    "F-HNNY");
if (wrongAircraft.Callsign is not null)
{
    failures.Add($"Indicatif d'un autre avion : rejet attendu, obtenu {wrongAircraft.Callsign}.");
}

var noContext = PhraseologyService.Analyze(
    "Poitiers Tour, Fox Golf Alpha Bravo Charlie, au parking pour tours de piste.");
if (!string.Equals(noContext.Callsign, "F-GABC", StringComparison.Ordinal))
{
    failures.Add($"Sans ATC ID : F-GABC attendu, obtenu {noContext.Callsign ?? "-"}.");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("PHONIE smoke tests FAILED");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("PHONIE smoke tests OK");

void RunFacilityDecoderTests()
{
    var packet = new byte[Phonie.Services.FacilityPacketDecoder.HeaderSize + Phonie.Services.FacilityPacketDecoder.TaxiPathPayloadSize];
    WriteUInt32(packet, 0, (uint)packet.Length);
    WriteUInt32(packet, 4, 4);
    WriteUInt32(packet, 8, 0x1234);
    WriteUInt32(packet, 12, 0x2501001);
    WriteUInt32(packet, 16, 0x6001);
    WriteUInt32(packet, 20, 0x5001);
    WriteInt32(packet, 24, 7);
    WriteUInt32(packet, 28, 1);
    WriteUInt32(packet, 32, 12);
    WriteUInt32(packet, 36, 20);

    var offset = Phonie.Services.FacilityPacketDecoder.HeaderSize;
    WriteInt32(packet, offset, 3); offset += 4;
    WriteSingle(packet, offset, 18.5f); offset += 4;
    WriteSingle(packet, offset, 9.25f); offset += 4;
    WriteSingle(packet, offset, 9.25f); offset += 4;
    WriteUInt32(packet, offset, 12_000); offset += 4;
    WriteInt32(packet, offset, 3); offset += 4;
    WriteInt32(packet, offset, 1); offset += 4;
    WriteInt32(packet, offset, 2); offset += 4;
    WriteInt32(packet, offset, 1); offset += 4;
    WriteInt32(packet, offset, 3); offset += 4;
    WriteInt32(packet, offset, 0); offset += 4;
    WriteInt32(packet, offset, 1); offset += 4;
    WriteInt32(packet, offset, 1); offset += 4;
    WriteInt32(packet, offset, 42); offset += 4;
    WriteInt32(packet, offset, 43); offset += 4;
    WriteUInt32(packet, offset, 5);

    var envelope = Phonie.Services.FacilityPacketDecoder.DecodeEnvelope(packet);
    if (envelope.DeclaredSize != packet.Length
        || envelope.UserRequestId != 0x2501001
        || envelope.UniqueRequestId != 0x6001
        || envelope.ParentUniqueRequestId != 0x5001
        || !envelope.IsListItem
        || envelope.ItemIndex != 12
        || envelope.ListSize != 20
        || envelope.PayloadLength != Phonie.Services.FacilityPacketDecoder.TaxiPathPayloadSize)
    {
        failures.Add("Décodeur Facilities : en-tête synthétique incorrect.");
    }

    var path = Phonie.Services.FacilityPacketDecoder.DecodeTaxiPath(packet, envelope, out var fields);
    if (path.Index != 12
        || path.Type != 3
        || Math.Abs(path.WidthMeters - 18.5f) > 0.001f
        || path.WeightLimit != 12_000
        || path.RunwayNumber != 3
        || path.RunwayDesignator != 1
        || path.StartIndex != 42
        || path.EndIndex != 43
        || path.NameIndex != 5
        || fields.Count != 16
        || fields[0].PacketOffset != Phonie.Services.FacilityPacketDecoder.HeaderSize
        || fields[^1].PacketOffset != packet.Length - 4)
    {
        failures.Add("Décodeur Facilities : charge utile TaxiPath synthétique incorrecte.");
    }

    var truncated = packet[..^4];
    ExpectInvalidData(
        "Décodeur Facilities : paquet TaxiPath tronqué accepté.",
        () =>
        {
            var truncatedEnvelope = Phonie.Services.FacilityPacketDecoder.DecodeEnvelope(truncated);
            _ = Phonie.Services.FacilityPacketDecoder.DecodeTaxiPath(truncated, truncatedEnvelope, out _);
        });

    var invalidDeclaredSize = packet.ToArray();
    WriteUInt32(invalidDeclaredSize, 0, (uint)(invalidDeclaredSize.Length + 4));
    var oversizedEnvelope = Phonie.Services.FacilityPacketDecoder.DecodeEnvelope(invalidDeclaredSize);
    if (oversizedEnvelope.SizeMatches)
    {
        failures.Add("Décodeur Facilities : taille déclarée supérieure au tampon non signalée.");
    }

    var mismatchedSize = packet.ToArray();
    WriteUInt32(mismatchedSize, 0, (uint)(mismatchedSize.Length - 4));
    var mismatchedEnvelope = Phonie.Services.FacilityPacketDecoder.DecodeEnvelope(mismatchedSize);
    if (mismatchedEnvelope.SizeMatches)
    {
        failures.Add("Décodeur Facilities : différence entre taille déclarée et reçue non signalée.");
    }
}

void ExpectInvalidData(string failureMessage, Action action)
{
    try
    {
        action();
        failures.Add(failureMessage);
    }
    catch (InvalidDataException)
    {
        // Résultat attendu.
    }
}

void WriteUInt32(byte[] buffer, int offset, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

void WriteInt32(byte[] buffer, int offset, int value) =>
    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), value);

void WriteSingle(byte[] buffer, int offset, float value) =>
    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

void Check(
    string name,
    string phrase,
    string simulatorCallsign,
    string? expectedStation = null,
    string? expectedCallsign = null,
    string? expectedPosition = null,
    string? expectedIntention = null,
    string? expectedAtis = null)
{
    var analysis = PhraseologyService.Analyze(phrase, simulatorCallsign);
    Assert(name, "station", expectedStation, analysis.CalledStation);
    Assert(name, "indicatif", expectedCallsign, analysis.Callsign);
    Assert(name, "position", expectedPosition, analysis.Position);
    Assert(name, "intention", expectedIntention, analysis.Intention);
    Assert(name, "ATIS", expectedAtis, analysis.AtisLetter);
}

void Assert(string name, string field, string? expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        failures.Add($"{name} - {field} : {expected ?? "-"} attendu, {actual ?? "-"} obtenu.");
    }
}

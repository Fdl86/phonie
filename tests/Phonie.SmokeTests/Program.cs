using System.Buffers.Binary;
using System.Text.Json;
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
RunManifestSerializationTests();
RunOperationalRadioTests();

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

void RunManifestSerializationTests()
{
    var manifest = new Phonie.Models.SiaRadioManifest
    {
        SchemaVersion = 2,
        DatasetId = "test",
        DatasetRevision = "revision",
        Authority = "SIA",
        GeneratedAt = DateTimeOffset.UtcNow,
        GeneratorVersion = "TEST",
        SourceCatalogUrl = "https://example.invalid/",
        BootstrapRequired = false,
    };

    var json = JsonSerializer.Serialize(manifest, Phonie.Models.SiaRadioManifestJson.Options);
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    if (!root.TryGetProperty("schemaVersion", out _)
        || !root.TryGetProperty("datasetRevision", out _)
        || root.TryGetProperty("SchemaVersion", out _))
    {
        failures.Add("Le manifest radio doit être sérialisé en camelCase stable.");
    }
}

void RunOperationalRadioTests()
{
    var status = OfficialRadioCatalogService.Reload(new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero));
    if (!status.Valid || status.AirportCount < 100)
    {
        failures.Add($"Fixture SIA non chargée : {status.Message}");
        return;
    }

    var report = new Phonie.Models.AirportFacilityReport { RequestedIcao = "LFXX", Icao = "LFXX" };
    // Données Facilities volontairement contradictoires : elles ne doivent jamais piloter un terrain français.
    report.Frequencies.Add(new Phonie.Models.AirportFrequencyData(0, 6, 119900000, 119.900, "FAUSSE TOUR MSFS"));
    var snapshot = new Phonie.Models.SimulatorSnapshot(
        new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero),
        "MSFS 2024", "Avion test", "F-HNNY", 47.0, -1.0, 300, 0, 0, 0, true,
        123.500, 118.700, "LFXX", "FSS", 1, true, 0, 0, 0, "7000",
        "LFXX", 0.1, "LFXX", "test", 210, 10, 1015, 15, 10, 9999, 3000);

    var active = OperationalRadioService.Resolve(snapshot, report, "LFXX");
    if (active.Kind != Phonie.Models.OperationalRadioKind.SelfInformation || active.DialogueAllowed)
    {
        failures.Add($"A/A SIA silencieuse : obtenu {active.Kind}, dialogue={active.DialogueAllowed}.");
    }

    var recommendation = OperationalRadioService.Recommend(report, "LFXX", isOnGround: true, snapshot.Timestamp);
    if (recommendation is null
        || recommendation.Kind != Phonie.Models.OperationalRadioKind.Controlled
        || Math.Abs(recommendation.FrequencyMhz - 118.700) > 0.001)
    {
        failures.Add($"Priorité Tour SIA : obtenu {recommendation?.ServiceName ?? "aucune"} {recommendation?.FrequencyMhz:F3}.");
    }

    var approach = OperationalRadioService.Recommend(null, "LFYY", isOnGround: true, snapshot.Timestamp);
    if (approach is null || Math.Abs(approach.FrequencyMhz - 124.800) > 0.001)
    {
        failures.Add("Priorité Approche SIA en absence de Tour non respectée.");
    }

    var wrongScene = OperationalRadioService.Resolve(snapshot with
    {
        Com1ActiveMhz = 119.900,
        Com1StationIdent = "LFBI",
        GeographicAirportIcao = "LFBI",
        RadioAirportIcao = "LFBI",
    }, report, "LFBI");
    if (wrongScene.Kind != Phonie.Models.OperationalRadioKind.Unknown || wrongScene.DialogueAllowed)
    {
        failures.Add("Une fréquence Facilities française absente de la base SIA ne doit jamais devenir dialoguée.");
    }

    var lfbiRecommendation = OperationalRadioService.Recommend(null, "LFBI", true, snapshot.Timestamp);
    if (lfbiRecommendation is null
        || Math.Abs(lfbiRecommendation.FrequencyMhz - 118.505) > 0.001
        || !lfbiRecommendation.ServiceName.Contains("TOUR", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("La Tour issue de la fixture SIA doit être prioritaire au sol.");
    }

    var mauleon = OperationalRadioService.Resolve(snapshot with
    {
        Com1ActiveMhz = 123.500,
        GeographicAirportIcao = "LFJB",
        RadioAirportIcao = "LFJB",
    }, null, "LFJB");
    if (mauleon.Kind != Phonie.Models.OperationalRadioKind.SelfInformation || mauleon.DialogueAllowed)
    {
        failures.Add("LFJB fixture : A/A doit rester silencieuse sans fréquence codée dans l'application.");
    }

    var montaiguFis = OperationalRadioService.Resolve(snapshot with
    {
        Com1ActiveMhz = 130.275,
        GeographicAirportIcao = "LFFW",
        RadioAirportIcao = "LFFW",
    }, null, "LFFW");
    if (montaiguFis.Kind != Phonie.Models.OperationalRadioKind.InformationService
        || !string.Equals(montaiguFis.Scope, "Regional", StringComparison.OrdinalIgnoreCase))
    {
        failures.Add("LFFW fixture : le FIS régional doit rester distinct de l'A/A locale.");
    }

    var montaiguLocal = OperationalRadioService.Recommend(null, "LFFW", true, snapshot.Timestamp);
    if (montaiguLocal is null
        || montaiguLocal.Kind != Phonie.Models.OperationalRadioKind.SelfInformation
        || Math.Abs(montaiguLocal.FrequencyMhz - 123.500) > 0.001)
    {
        failures.Add("LFFW fixture : la recommandation locale doit rester l'A/A, pas le FIS régional.");
    }

    var montaiguInFlight = OperationalRadioService.Recommend(null, "LFFW", false, snapshot.Timestamp);
    if (montaiguInFlight is null
        || montaiguInFlight.Kind != Phonie.Models.OperationalRadioKind.InformationService
        || Math.Abs(montaiguInFlight.FrequencyMhz - 130.275) > 0.001)
    {
        failures.Add("LFFW fixture : le FIS régional doit devenir prioritaire en vol.");
    }
}

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

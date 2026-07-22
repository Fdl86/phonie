using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Phonie.Core;

var tests = new List<(string Name, Action Test)>
{
    ("graphe taxi valide", TestGraphBuild),
    ("capture LFBI MSFS 2024 exploitable", TestLfbiMsfs2024Fixture),
    ("tous parkings LFBI reliés à une attente", TestLfbiAllParkingsReachHold),
    ("capture LFBI MSFS 2020 normalisée", TestLfbiMsfs2020Fixture),
    ("profil LFBI enrichit A2 et A3 sans piloter le routage", TestLfbiOperationalProfile),
    ("phraséologie LFBI générique vers le point d'attente", TestLfbiConciseTaxiPhraseology),
    ("prêt au point d'attente donne alignement et décollage", TestLfbiDirectTakeoffFromHold),
    ("nom de point annoncé ne pilote pas la clairance", TestReportedPointDoesNotDriveClearance),
    ("tout vrai point d'attente donne accès à la séquence départ", TestAnyFacilitiesHoldClearsTakeoff),
    ("piste occupée bloque la clairance décollage", TestOccupiedRunwayBlocksTakeoff),
    ("trafic inconnu bloque la clairance décollage", TestUnknownTrafficBlocksTakeoff),
    ("AFIS informe sans clairance de contrôle", TestAfisInformationOnly),
    ("AFIS au point d'attente reste informatif", TestAfisReadyInformationOnly),
    ("collationnement PTT et relance", TestAcknowledgementLifecycle),
    ("chemin parking vers attente", TestParkingToHoldRoute),
    ("attente libre la plus proche", TestNearestAvailableHold),
    ("attente occupée exclue", TestOccupiedHoldExcluded),
    ("absence itinéraire signalée", TestNoRoute),
    ("avion utilisateur exclu de l'occupation", TestUserAircraftExcludedFromOccupancy),
    ("avions stationnés voisins ne bloquent pas la sortie LFBI", TestParkedNeighboursDoNotBlockLfbiRoute),
    ("trafic sur axe bloque seulement le segment proche", TestTrafficOnTaxiwayBlocksNearestEdge),
    ("trafic immobile sur taxiway reste bloquant", TestStationaryTrafficOnTaxiwayRemainsBlocking),
    ("demande roulage reconnue", () => Assert(PilotIntentParser.Parse("demande roulage") == PilotIntent.TaxiRequest)),
    ("prêt au départ reconnu au point d'attente", () => Assert(PilotIntentParser.Parse("Fox Novembre Yankee prêt au départ") == PilotIntent.ReadyAtHoldShort)),
    ("A2 prêt depuis intersection reconnu", () => Assert(PilotIntentParser.Parse("Fox Novembre Yankee A2 prêt pour un départ depuis l'intersection") == PilotIntent.ReadyForIntersectionDeparture)),
    ("prêt en point alphanumérique générique reconnu", () => Assert(PilotIntentParser.ParseDetailed("Fox Novembre Yankee prêt en E4").ReportedPoint == "E4")),
    ("lettre d'indicatif non prise pour un point", () => Assert(PilotIntentParser.ParseDetailed("Fox Alpha Bravo Charlie Delta prêt au départ").ReportedPoint is null)),
    ("alignement et décollage distinct", () => Assert(PilotIntentParser.Parse("demande alignement et décollage") == PilotIntent.LineUpAndTakeoffRequest)),
    ("décollage parking refusé", TestTakeoffFromParkingRefused),
    ("indicatif complet premier contact", TestFullCallsignInitialContact),
    ("indicatif abrégé après contact", TestShortCallsignAfterContact),
    ("première demande roulage traite bonjour sans répétition", TestFirstTaxiMessageAndGreetingHistory),
    ("bonjour non répété sur le même organisme", TestGreetingNotRepeatedOnSameStation),
    ("changement Sol Tour ouvre un nouveau contact", TestGroundToTowerNewContact),
    ("retour après ATIS conserve le contact Tour", TestAtisReturnKeepsTowerContact),
    ("de retour avec vous est reconnu comme reprise de contact", TestReturnGreetingIntent),
    ("réinitialisation du contexte conserve l'historique", TestGroundContextResetKeepsHistory),
    ("nouvelle session efface l'historique", TestFlightSessionResetClearsHistory),
    ("appel clair d'un autre service reste silencieux", TestClearlyCalledOtherServiceSilent),
    ("appel clair d'un autre aérodrome reste silencieux", TestClearlyCalledOtherAirportSilent),
    ("SIV régional refuse les demandes sol", TestRegionalFisRejectsGroundRequest),
    ("ATIS sans dialogue", TestAtisSilent),
    ("CTAF sans dialogue", TestCtafSilent),
    ("station inconnue sans clairance", TestUnknownStationSilent),
    ("TaxiPath non-piste ignore piste corrompue", TestNonRunwayGarbageIgnored),
    ("AirportList MSFS 2024 accepte l'emplacement de compatibilité", TestAirportListMsfs2024CompatibilitySlot),
    ("contexte géographique suit le nouvel aérodrome", TestGeographicAirportSelection),
    ("téléportation abandonne l'ancien aérodrome", TestTeleportAirportSelection),
    ("contexte radio peut viser un autre aérodrome", TestRadioContextByStationIdent),
    ("position station COM résout l'aérodrome radio", TestRadioContextByStationPosition),
    ("fréquence Facilities résout l'aérodrome radio", TestRadioContextByFrequency),
    ("station COM proche secourt la détection au sol", TestOnGroundStationFallback),
    ("routage secourt les associations piste absentes", TestHoldRoutingWithoutRunwayAssociation),
    ("priorité radio Tour sur A/A et Approche", TestRadioPriorityTower),
    ("priorité radio Approche sans Tour", TestRadioPriorityApproach),
    ("A/A seule ne devient jamais dialoguée", TestRadioSelfInformationOnly),
    ("catalogue SIA résout une fréquence partagée par contexte ICAO", TestSiaSharedChannelByAirport),
    ("catalogue SIA préfère Tour à Approche au sol", TestSiaRecommendationPriority),
    ("catalogue SIA recommande l'Approche régionale sans Tour locale", TestSiaRegionalRecommendationPriority),
    ("catalogue SIA recommande le FIS régional en vol", TestSiaFlightInformationPriority),
    ("portée locale départage deux services de même priorité", TestSiaScopeTieBreak),
    ("horaires non évalués sur canal partagé imposent le silence", TestSiaAmbiguousScheduleSafety),
    ("Facilities Tour départage un canal SIA Tour A-A", TestSiaFacilitiesTowerTieBreak),
    ("canalisation 8,33 utilise une représentation entière", TestSiaChannelNormalization),
    ("segments cache Windows neutralisent noms réservés et suffixes interdits", TestWindowsPathSegment),
};

var failures = new List<string>();
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS - {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.Message}");
        Console.WriteLine($"FAIL - {name}: {exception.Message}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) en échec.");
    Environment.Exit(1);
}

Console.WriteLine($"PHONIE Core tests OK - {tests.Count}/{tests.Count}");




static void TestSiaSharedChannelByAirport()
{
    var catalogue = BuildSiaTestCatalog();
    var first = catalogue.Resolve("LFXX", 123.500, preferLocal: true);
    var second = catalogue.Resolve("LFYY", 123.500, preferLocal: true);
    Assert(first.FrequencyKnown && first.Frequency?.Callsign == "ALPHA A/A", first.Reason);
    Assert(second.FrequencyKnown && second.Frequency?.Callsign == "BRAVO A/A", second.Reason);
}

static void TestSiaRecommendationPriority()
{
    var catalogue = BuildSiaTestCatalog();
    var recommended = catalogue.Recommend("LFXX", isOnGround: true, dialogueOnly: false);
    Assert(recommended?.Kind == SiaRadioServiceKind.Tower, recommended?.Callsign ?? "aucune recommandation");
}

static void TestSiaRegionalRecommendationPriority()
{
    var catalogue = BuildSiaTestCatalog();
    var recommended = catalogue.Recommend("LFYY", isOnGround: true, dialogueOnly: false);
    Assert(recommended?.Kind == SiaRadioServiceKind.Approach, recommended?.Callsign ?? "aucune recommandation");
}

static void TestSiaFlightInformationPriority()
{
    var dataset = BuildSiaDataset();
    dataset.Airports.Add(new SiaAirportRadioRecord
    {
        Icao = "LFWW",
        Name = "DELTA",
        Frequencies = new List<SiaRadioFrequencyRecord>
        {
            new()
            {
                Channel = "123.500", ChannelKhz = 123500, ServiceCode = "A/A", Callsign = "DELTA A/A",
                Kind = SiaRadioServiceKind.SelfInformation, Scope = SiaRadioStationScope.Local,
                Interactive = false, ScheduleState = SiaRadioScheduleState.NotApplicable,
            },
            new()
            {
                Channel = "130.275", ChannelKhz = 130275, ServiceCode = "FIS", Callsign = "DELTA INFORMATION",
                Kind = SiaRadioServiceKind.FlightInformation, Scope = SiaRadioStationScope.Regional,
                Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
            },
        },
    });

    var recommended = new SiaRadioCatalog(dataset).Recommend("LFWW", isOnGround: false, dialogueOnly: false);
    Assert(recommended?.Kind == SiaRadioServiceKind.FlightInformation, recommended?.Callsign ?? "aucune recommandation");
}

static void TestSiaScopeTieBreak()
{
    var dataset = BuildSiaDataset();
    dataset.Airports.Add(new SiaAirportRadioRecord
    {
        Icao = "LFZZ",
        Name = "CHARLIE",
        Frequencies = new List<SiaRadioFrequencyRecord>
        {
            new()
            {
                Channel = "124.100", ChannelKhz = 124100, ServiceCode = "INFO", Callsign = "CHARLIE REGIONAL",
                Kind = SiaRadioServiceKind.Information, Scope = SiaRadioStationScope.Regional,
                Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
            },
            new()
            {
                Channel = "124.200", ChannelKhz = 124200, ServiceCode = "INFO", Callsign = "CHARLIE LOCAL",
                Kind = SiaRadioServiceKind.Information, Scope = SiaRadioStationScope.Local,
                Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
            },
        },
    });

    var recommended = new SiaRadioCatalog(dataset).Recommend("LFZZ", isOnGround: true, dialogueOnly: false);
    Assert(recommended?.Scope == SiaRadioStationScope.Local, recommended?.Callsign ?? "aucune recommandation");
}

static void TestSiaAmbiguousScheduleSafety()
{
    var dataset = BuildSiaDataset();
    var airport = dataset.Airports.Single(item => item.Icao == "LFXX");
    airport.Frequencies.Add(new SiaRadioFrequencyRecord
    {
        Channel = "120.405",
        ChannelKhz = 120405,
        ServiceCode = "AFIS",
        Callsign = "ALPHA INFORMATION",
        Kind = SiaRadioServiceKind.Information,
        Scope = SiaRadioStationScope.Local,
        Interactive = true,
        ScheduleState = SiaRadioScheduleState.PublishedNotEvaluated,
    });
    airport.Frequencies.Add(new SiaRadioFrequencyRecord
    {
        Channel = "120.405",
        ChannelKhz = 120405,
        ServiceCode = "A/A",
        Callsign = "ALPHA A/A",
        Kind = SiaRadioServiceKind.SelfInformation,
        Scope = SiaRadioStationScope.Local,
        Interactive = false,
        ScheduleState = SiaRadioScheduleState.NotApplicable,
    });
    var resolution = new SiaRadioCatalog(dataset).Resolve("LFXX", 120.405, preferLocal: true);
    Assert(resolution.Ambiguous, resolution.Reason);
    Assert(resolution.Frequency is { Interactive: false }, "le mode silencieux n'a pas été retenu");
}

static void TestSiaFacilitiesTowerTieBreak()
{
    var dataset = BuildSiaDataset();
    var airport = dataset.Airports.Single(item => item.Icao == "LFXX");
    airport.Frequencies.Add(new SiaRadioFrequencyRecord
    {
        Channel = "118.505", ChannelKhz = 118505, ServiceCode = "A/A", Callsign = "ALPHA AUTO-INFORMATION",
        Kind = SiaRadioServiceKind.SelfInformation, Scope = SiaRadioStationScope.Local,
        Interactive = false, ScheduleState = SiaRadioScheduleState.NotApplicable,
    });
    airport.Frequencies.Single(item => item.ServiceCode == "TWR").ScheduleState = SiaRadioScheduleState.PublishedNotEvaluated;

    var catalogue = new SiaRadioCatalog(dataset);
    var safe = catalogue.Resolve("LFXX", 118.505, preferLocal: true);
    Assert(safe.Ambiguous && safe.Frequency is { Interactive: false }, safe.Reason);

    var confirmed = catalogue.Resolve("LFXX", 118.505, preferLocal: true, SiaRadioServiceKind.Tower);
    Assert(!confirmed.Ambiguous, confirmed.Reason);
    Assert(confirmed.ServiceConfirmed, confirmed.Reason);
    Assert(confirmed.Frequency?.Kind == SiaRadioServiceKind.Tower, confirmed.Frequency?.Callsign);
}

static void TestSiaChannelNormalization()
{
    Assert(RadioChannel.ToChannelKhz(118.505) == 118505);
    Assert(RadioChannel.CarrierHzFromChannelKhz(118505) == 118500000);
    var record = new SiaRadioFrequencyRecord { ChannelKhz = 118505, Channel = "118.505" };
    Assert(RadioChannel.Matches(record, 118.505));
}

static void TestWindowsPathSegment()
{
    Assert(WindowsPathSegment.Sanitize("COM1") == "_COM1");
    Assert(WindowsPathSegment.Sanitize("com1.cache") == "_com1.cache");
    Assert(WindowsPathSegment.Sanitize("LFBI|TOUR. ") == "LFBI_TOUR");
    Assert(!WindowsPathSegment.Sanitize(new string('X', 200)).EndsWith(".", StringComparison.Ordinal));
    Assert(WindowsPathSegment.Sanitize("..") == "station");
}

static SiaRadioCatalog BuildSiaTestCatalog() => new(BuildSiaDataset());

static SiaRadioDataset BuildSiaDataset()
{
    var from = new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);
    return new SiaRadioDataset
    {
        Authority = "SIA",
        AiracCycle = "TEST",
        Revision = "fixture",
        EffectiveFrom = from,
        EffectiveUntil = from.AddDays(28),
        Airports = new List<SiaAirportRadioRecord>
        {
            new()
            {
                Icao = "LFXX",
                Name = "ALPHA",
                Frequencies = new List<SiaRadioFrequencyRecord>
                {
                    new()
                    {
                        Channel = "123.500", ChannelKhz = 123500, ServiceCode = "A/A", Callsign = "ALPHA A/A",
                        Kind = SiaRadioServiceKind.SelfInformation, Scope = SiaRadioStationScope.Local,
                        Interactive = false, ScheduleState = SiaRadioScheduleState.NotApplicable,
                    },
                    new()
                    {
                        Channel = "118.505", ChannelKhz = 118505, ServiceCode = "TWR", Callsign = "ALPHA TOUR",
                        Kind = SiaRadioServiceKind.Tower, Scope = SiaRadioStationScope.Local,
                        Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
                    },
                    new()
                    {
                        Channel = "134.100", ChannelKhz = 134100, ServiceCode = "APP", Callsign = "ALPHA APPROCHE",
                        Kind = SiaRadioServiceKind.Approach, Scope = SiaRadioStationScope.Regional,
                        Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
                    },
                },
            },
            new()
            {
                Icao = "LFYY",
                Name = "BRAVO",
                Frequencies = new List<SiaRadioFrequencyRecord>
                {
                    new()
                    {
                        Channel = "123.500", ChannelKhz = 123500, ServiceCode = "A/A", Callsign = "BRAVO A/A",
                        Kind = SiaRadioServiceKind.SelfInformation, Scope = SiaRadioStationScope.Local,
                        Interactive = false, ScheduleState = SiaRadioScheduleState.NotApplicable,
                    },
                    new()
                    {
                        Channel = "124.800", ChannelKhz = 124800, ServiceCode = "APP", Callsign = "BRAVO APPROCHE",
                        Kind = SiaRadioServiceKind.Approach, Scope = SiaRadioStationScope.Regional,
                        Interactive = true, ScheduleState = SiaRadioScheduleState.Always,
                    },
                },
            },
        },
    };
}

static void TestAirportListMsfs2024CompatibilitySlot()
{
    foreach (var declaredCount in new[] { 161, 204 })
    {
        var packetBytes = BuildAirportListPacket(declaredCount, includeCompatibilitySlot: true);
        var packet = AirportListPacketDecoder.Decode(packetBytes);

        var expectedPayloadLength = declaredCount == 161 ? 5832 : 7380;
        Assert(
            packetBytes.Length - AirportListPacketDecoder.HeaderSize == expectedPayloadLength,
            $"charge utile={packetBytes.Length - AirportListPacketDecoder.HeaderSize}");
        Assert(packet.DeclaredArraySize == (uint)declaredCount, $"déclaré={packet.DeclaredArraySize}");
        Assert(packet.DecodedSlotCount == declaredCount + 1, $"emplacements={packet.DecodedSlotCount}");
        Assert(packet.CompatibilitySlotCount == 1, $"compatibilité={packet.CompatibilitySlotCount}");
        Assert(packet.EntryStride == AirportListPacketDecoder.Msfs2024EntrySize, $"stride={packet.EntryStride}");
        Assert(packet.Airports.Any(item => item.Icao == "LFOU"), "LFOU absent");
        Assert(packet.Airports.Any(item => item.Icao == "LFBI"), "LFBI absent");
        Assert(packet.Airports.All(item => item.Icao != "ZZZZ"), "emplacement de compatibilité décodé à tort");
    }
}

static byte[] BuildAirportListPacket(int declaredCount, bool includeCompatibilitySlot)
{
    var slotCount = declaredCount + (includeCompatibilitySlot ? 1 : 0);
    var buffer = new byte[
        AirportListPacketDecoder.HeaderSize
        + (slotCount * AirportListPacketDecoder.Msfs2024EntrySize)];
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), (uint)buffer.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4, 4), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8, 4), 18);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12, 4), 0x06101001);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16, 4), (uint)declaredCount);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20, 4), 0);
    BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24, 4), 1);

    for (var index = 0; index < declaredCount; index++)
    {
        var icao = index switch
        {
            0 => "LFOU",
            1 => "LFBI",
            _ => $"X{index % 1000:000}",
        };
        var offset = AirportListPacketDecoder.HeaderSize
            + (index * AirportListPacketDecoder.Msfs2024EntrySize);
        Encoding.ASCII.GetBytes(icao).AsSpan().CopyTo(buffer.AsSpan(offset, 4));
        Encoding.ASCII.GetBytes("LF").AsSpan().CopyTo(buffer.AsSpan(offset + 9, 2));
        WriteDouble(buffer, offset + 12, 47.0810 + (index * 0.00001));
        WriteDouble(buffer, offset + 20, -0.8770 + (index * 0.00001));
        WriteDouble(buffer, offset + 28, 135.0);
    }

    if (includeCompatibilitySlot)
    {
        var offset = AirportListPacketDecoder.HeaderSize
            + (declaredCount * AirportListPacketDecoder.Msfs2024EntrySize);
        Encoding.ASCII.GetBytes("ZZZZ").AsSpan().CopyTo(buffer.AsSpan(offset, 4));
        Encoding.ASCII.GetBytes("ZZ").AsSpan().CopyTo(buffer.AsSpan(offset + 9, 2));
        WriteDouble(buffer, offset + 12, 0.0);
        WriteDouble(buffer, offset + 20, 0.0);
        WriteDouble(buffer, offset + 28, 0.0);
    }

    return buffer;
}

static void WriteDouble(byte[] buffer, int offset, double value) =>
    BinaryPrimitives.WriteInt64LittleEndian(
        buffer.AsSpan(offset, 8),
        BitConverter.DoubleToInt64Bits(value));

static void TestGeographicAirportSelection()
{
    var selection = AirportContextResolver.Resolve(
        BuildAirportCandidates(),
        47.0810,
        -0.8770,
        true,
        "LFBI",
        string.Empty,
        0,
        null,
        null);

    Assert(selection.GeographicIcao == "LFOU", selection.GeographicIcao);
    Assert(double.IsFinite(selection.GeographicDistanceNm));
}

static void TestTeleportAirportSelection()
{
    var airports = BuildAirportCandidates();
    var before = AirportContextResolver.Resolve(
        airports,
        46.5870,
        0.3070,
        true,
        string.Empty,
        string.Empty,
        0,
        null,
        null);
    var after = AirportContextResolver.Resolve(
        airports,
        47.0810,
        -0.8770,
        true,
        before.GeographicIcao,
        string.Empty,
        0,
        null,
        null);

    Assert(before.GeographicIcao == "LFBI", before.GeographicIcao);
    Assert(after.GeographicIcao == "LFOU", after.GeographicIcao);
}

static void TestRadioContextByStationIdent()
{
    var selection = AirportContextResolver.Resolve(
        BuildAirportCandidates(),
        47.0810,
        -0.8770,
        false,
        "LFOU",
        "LFBI",
        134.100,
        null,
        null);

    Assert(selection.GeographicIcao == "LFOU", selection.GeographicIcao);
    Assert(selection.RadioIcao == "LFBI", selection.RadioIcao);
    Assert(selection.RadioSource.Contains("Identifiant COM", StringComparison.Ordinal));
}

static void TestRadioContextByStationPosition()
{
    var selection = AirportContextResolver.Resolve(
        BuildAirportCandidates(),
        47.0810,
        -0.8770,
        false,
        "LFOU",
        string.Empty,
        134.100,
        46.5870,
        0.3070);

    Assert(selection.GeographicIcao == "LFOU", selection.GeographicIcao);
    Assert(selection.RadioIcao == "LFBI", selection.RadioIcao);
    Assert(selection.RadioSource.Contains("Position", StringComparison.Ordinal));
}

static void TestRadioContextByFrequency()
{
    var selection = AirportContextResolver.Resolve(
        BuildAirportCandidates(),
        47.0810,
        -0.8770,
        false,
        "LFOU",
        string.Empty,
        134.100,
        null,
        null);

    Assert(selection.GeographicIcao == "LFOU", selection.GeographicIcao);
    Assert(selection.RadioIcao == "LFBI", selection.RadioIcao);
    Assert(selection.RadioSource.Contains("Fréquence", StringComparison.Ordinal));
}

static void TestOnGroundStationFallback()
{
    var selection = AirportContextResolver.Resolve(
        Array.Empty<NearbyAirportCandidate>(),
        47.0810,
        -0.8770,
        true,
        string.Empty,
        "LFOU",
        120.400,
        47.0811,
        -0.8771);

    Assert(selection.GeographicIcao == "LFOU", selection.GeographicIcao);
    Assert(selection.RadioIcao == "LFOU", selection.RadioIcao);
}

static void TestHoldRoutingWithoutRunwayAssociation()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var runway = model.RunwayEnds.Single(item => item.Designator == "03") with
    {
        RunwayIndex = 999,
    };
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:12", null, 0, 1, "Parking S6"),
        runway,
        AvailableOccupancy());

    Assert(route.Success, route.FailureReason);
    Assert(route.HoldShort is not null);
}

static void TestRadioPriorityTower()
{
    var recommendation = AirportRadioSelector.Recommend(
        new[]
        {
            new AirportRadioCandidate(3, 123.500, "A/A"),
            new AirportRadioCandidate(8, 124.800, "APPROCHE"),
            new AirportRadioCandidate(6, 118.700, "TOUR"),
        },
        isOnGround: true);

    Assert(recommendation is not null);
    Assert(recommendation!.Kind == AirportRadioServiceKind.Tower, recommendation.Kind.ToString());
    Assert(Math.Abs(recommendation.FrequencyMhz - 118.700) < 0.001, recommendation.FrequencyMhz.ToString("F3"));
}

static void TestRadioPriorityApproach()
{
    var recommendation = AirportRadioSelector.Recommend(
        new[]
        {
            new AirportRadioCandidate(3, 123.500, "A/A"),
            new AirportRadioCandidate(8, 124.800, "APPROCHE"),
        },
        isOnGround: true);

    Assert(recommendation is not null);
    Assert(recommendation!.Kind == AirportRadioServiceKind.Approach, recommendation.Kind.ToString());
}

static void TestRadioSelfInformationOnly()
{
    var recommendation = AirportRadioSelector.Recommend(
        new[]
        {
            new AirportRadioCandidate(3, 123.500, "A/A"),
            new AirportRadioCandidate(2, 122.800, "CTAF"),
        },
        isOnGround: true);

    Assert(recommendation is null, recommendation?.Name);
    Assert(AirportRadioSelector.IsSilent(AirportRadioServiceKind.SelfInformation));
}

static IReadOnlyList<NearbyAirportCandidate> BuildAirportCandidates() => new[]
{
    new NearbyAirportCandidate("LFBI", "LF", 46.5870, 0.3070, 129, new[] { 118.505, 121.780, 134.100 }),
    new NearbyAirportCandidate("LFOU", "LF", 47.0810, -0.8770, 135, new[] { 120.400 }),
};

static void TestLfbiMsfs2024Fixture()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    Assert(model.IsUsable);
    Assert(model.Nodes.Count == 148, $"nœuds={model.Nodes.Count}");
    Assert(model.Edges.Count == 154, $"segments={model.Edges.Count}");
    Assert(model.HoldShortPoints.Count == 3, $"attentes={model.HoldShortPoints.Count}");
    Assert(model.RunwayEnds.Count == 6, $"extrémités pistes={model.RunwayEnds.Count}");

    var runway03 = model.RunwayEnds.Single(item => item.Designator == "03");
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:0", null, 0, 1, "Parking 2"),
        runway03,
        AvailableOccupancy());
    Assert(route.Success, route.FailureReason);
    Assert(route.HoldShort is not null);
    Assert(route.HoldShort!.AssociatedRunwayIndex == runway03.RunwayIndex);
    Assert(route.TaxiwayNames.Count > 0);
}

static void TestLfbiAllParkingsReachHold()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var runway03 = model.RunwayEnds.Single(item => item.Designator == "03");

    for (var parkingIndex = 0; parkingIndex < 23; parkingIndex++)
    {
        var route = TaxiRouter.RouteToNearestAvailableHoldShort(
            model,
            new GroundLocation(
                GroundPositionKind.Parking,
                $"P:{parkingIndex}",
                null,
                0,
                1,
                $"Parking {parkingIndex}"),
            runway03,
            AvailableOccupancy());
        Assert(route.Success, $"Parking P:{parkingIndex} - {route.FailureReason}");
    }
}


static void TestLfbiOperationalProfile()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var runway21 = model.RunwayEnds.Single(item => item.Designator == "21");
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:12", null, 0, 1, "Parking S6"),
        runway21,
        AvailableOccupancy(),
        profile);

    Assert(route.Success, route.FailureReason);
    Assert(route.HoldShort is not null);
    Assert(!route.IncludeViaInSpeech);
    var resolutions = OperationalPointResolver.Resolve(model, profile);
    Assert(resolutions["T:17"].Role == OperationalPointRole.DepartureHoldingPoint);
    Assert(resolutions["T:17"].RadioLabel == "A2");
    Assert(resolutions["T:99"].Role == OperationalPointRole.IntermediateHoldingPoint);
    Assert(resolutions["T:99"].RadioLabel == "A3");
    Assert(route.OperationalPoint?.RadioLabel == "A3", $"point choisi={route.OperationalPoint?.RadioLabel}");
    // Le profil enrichit l'affichage uniquement. Une entrée locale non associée
    // ne doit jamais bloquer le routage générique ni la séquence de départ.
}

static void TestLfbiConciseTaxiPhraseology()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = new GroundOperationsEngine();
    _ = engine.Process(
        "Poitiers Tour bonjour au parking",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    var decision = engine.Process(
        "prêt au roulage",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.Action == ControllerAction.Speak, decision.SystemMessage);
    Assert(decision.ReasonCode == "TAXI_CLEARANCE_GENERIC_HOLD", decision.ReasonCode);
    Assert(decision.SpokenText.Contains("roulez au point d'attente et rappelez prêt", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("Alpha", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("Delta", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("via ", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(decision.SystemMessage.Contains("Itinéraire interne calculé", StringComparison.Ordinal), decision.SystemMessage);
    Assert(decision.RequiresAcknowledgement);
}

static void TestLfbiDirectTakeoffFromHold()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = new GroundOperationsEngine();
    _ = engine.Process(
        "Poitiers Tour bonjour au parking",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    _ = engine.Process(
        "prêt au roulage",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    var decision = engine.Process(
        "Fox Novembre Yankee prêt au point d'attente",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "T:17"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.Action == ControllerAction.Speak, decision.SystemMessage);
    Assert(decision.ReasonCode == "LINEUP_TAKEOFF_CLEARED_FROM_HOLD", decision.ReasonCode);
    // Vérifier chaque élément opérationnel sans figer la tournure complète.
    Assert(decision.SpokenText.Contains("piste deux un", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.SpokenText.Contains("alignez-vous", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.SpokenText.Contains("autorisé décollage", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.SpokenText.Contains("vent deux un zéro degrés, un zéro nœuds", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("Alpha", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("intersection", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(decision.RequiresAcknowledgement);
}

static void TestReportedPointDoesNotDriveClearance()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = PrepareLfbiTaxiSession(model, profile);
    var decision = engine.Process(
        "Fox Novembre Yankee prêt en Bravo 7",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "T:17"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.ReasonCode == "LINEUP_TAKEOFF_CLEARED_FROM_HOLD", decision.ReasonCode);
    Assert(!decision.SpokenText.Contains("Bravo", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
}

static void TestAnyFacilitiesHoldClearsTakeoff()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = PrepareLfbiTaxiSession(model, profile);
    var decision = engine.Process(
        "Fox Novembre Yankee prêt au point d'attente",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "T:99"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.Action == ControllerAction.Speak, decision.SpokenText);
    Assert(decision.ReasonCode == "LINEUP_TAKEOFF_CLEARED_FROM_HOLD", decision.ReasonCode);
    Assert(decision.SpokenText.Contains("autorisé décollage", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
}

static void TestOccupiedRunwayBlocksTakeoff()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = PrepareLfbiTaxiSession(model, profile);
    var runway = engine.Session.AssignedRunway ?? throw new InvalidOperationException("Piste non attribuée.");
    var occupiedEdges = model.Edges
        .Where(item => item.IsRunway && (item.RunwayNumber == runway.Number || item.RunwayNumber is null))
        .Select(item => item.SourceIndex)
        .ToHashSet();
    Assert(occupiedEdges.Count > 0, "Aucun segment piste trouvé.");
    var occupied = new GroundOccupancySnapshot(
        DateTimeOffset.UtcNow,
        OccupancyKnowledge.Available,
        new HashSet<string>(StringComparer.Ordinal),
        occupiedEdges,
        "test piste occupée");
    var decision = engine.Process(
        "Fox Novembre Yankee prêt au point d'attente",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "T:17"),
        occupied,
        210,
        10,
        profile,
        1015);

    Assert(decision.ReasonCode == "RUNWAY_OCCUPIED_HOLD", decision.ReasonCode);
    Assert(decision.SpokenText.Contains("maintenez point d'attente", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("autorisé décollage", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
}

static void TestUnknownTrafficBlocksTakeoff()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = PrepareLfbiTaxiSession(model, profile);
    var decision = engine.Process(
        "Fox Novembre Yankee prêt au point d'attente",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "T:17"),
        GroundOccupancySnapshot.Unknown(DateTimeOffset.UtcNow, "test indisponible"),
        210,
        10,
        profile,
        1015);

    Assert(decision.ReasonCode == "TRAFFIC_STATUS_UNKNOWN_HOLD", decision.ReasonCode);
    Assert(decision.SpokenText.Contains("trafic non déterminé", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("autorisé décollage", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
}

static void TestAfisInformationOnly()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var decision = new GroundOperationsEngine().Process(
        "prêt au roulage",
        "F-HNNY",
        new RadioContext(ServiceCapability.InformationOnly, "Terrain AFIS", true, "test"),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.Action == ControllerAction.Speak, decision.SystemMessage);
    Assert(decision.ReasonCode == "AFIS_TAXI_INFORMATION");
    Assert(!decision.SpokenText.Contains("roulez", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("autorisé", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("Alpha", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(decision.SpokenText.Contains("rappelez prêt au point d'attente", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.RequiresAcknowledgement);
}

static void TestAfisReadyInformationOnly()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var profile = BuildLfbiProfile();
    var engine = new GroundOperationsEngine();
    var afis = new RadioContext(ServiceCapability.InformationOnly, "Terrain AFIS", true, "test");
    _ = engine.Process(
        "prêt au roulage",
        "F-HNNY",
        afis,
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    var decision = engine.Process(
        "Fox Novembre Yankee prêt au point d'attente",
        "F-HNNY",
        afis,
        model,
        ObservationAtNode(model, "T:17"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);

    Assert(decision.ReasonCode == "AFIS_READY_INFORMATION", decision.ReasonCode);
    Assert(decision.SpokenText.Contains("aucun trafic signalé", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("alignez-vous", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("autorisé", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(decision.RequiresAcknowledgement);
}

static void TestAcknowledgementLifecycle()
{
    var engine = new GroundOperationsEngine();
    var decision = engine.Process(
        "Poitiers Tour bonjour au parking",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.RequiresAcknowledgement);

    var now = DateTimeOffset.UtcNow;
    engine.ArmPilotAcknowledgement(now, TimeSpan.FromSeconds(1));
    Assert(engine.Session.AwaitingPilotAcknowledgement);
    Assert(engine.PollAcknowledgement(now + TimeSpan.FromMilliseconds(500)) is null);
    var reminder = engine.PollAcknowledgement(now + TimeSpan.FromSeconds(2));
    Assert(reminder is not null);
    Assert(reminder!.ReasonCode == "ACKNOWLEDGEMENT_REMINDER");
    Assert(!reminder.RequiresAcknowledgement, "La relance ne doit pas réarmer et remettre le compteur à zéro.");
    Assert(engine.AcknowledgePilotPtt());
    Assert(!engine.Session.AwaitingPilotAcknowledgement);
}

static void TestLfbiMsfs2020Fixture()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2020-ground.json"));
    Assert(model.IsUsable);
    Assert(model.Edges.Count == 154, $"segments={model.Edges.Count}");
    Assert(model.Edges
        .Where(edge => edge.Kind is TaxiPathKind.Taxi or TaxiPathKind.Parking or TaxiPathKind.Path)
        .All(edge => edge.RunwayNumber is null && edge.RunwayDesignator is null));
    Assert(model.HoldShortPoints.Count == 3, $"attentes={model.HoldShortPoints.Count}");
}

static FacilityAirportSnapshot LoadFixture(string fileName)
{
    var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<FacilityAirportSnapshot>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    }) ?? throw new InvalidDataException($"Fixture illisible : {fileName}");
}

static void TestGraphBuild()
{
    var model = BuildAirport();
    Assert(model.IsUsable);
    Assert(model.Nodes.Count == 9, $"nœuds={model.Nodes.Count}");
    Assert(model.HoldShortPoints.Count == 2, $"attentes={model.HoldShortPoints.Count}");
    Assert(model.Edges.Any(edge => edge.Kind == TaxiPathKind.Parking));
}

static void TestParkingToHoldRoute()
{
    var model = BuildAirport();
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:0", null, 0, 1, "Parking"),
        model.RunwayEnds.Single(item => item.Number == 21),
        AvailableOccupancy());
    Assert(route.Success, route.FailureReason);
    Assert(route.Edges.Count >= 3);
    Assert(route.HoldShort is not null);
    Assert(route.HoldShort!.Label == "D1", $"attente={route.HoldShort.Label}");
    Assert(route.TaxiwayNames.SequenceEqual(new[] { "D" }),
        $"via={string.Join(",", route.TaxiwayNames)}");
}

static void TestNearestAvailableHold()
{
    var model = BuildAirport();
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:0", null, 0, 1, "Parking"),
        model.RunwayEnds.Single(item => item.Number == 21),
        AvailableOccupancy());
    Assert(route.Success);
    Assert(route.HoldShort?.NodeId == "T:3", $"attente={route.HoldShort?.NodeId}");
}

static void TestOccupiedHoldExcluded()
{
    var model = BuildAirport();
    var occupied = new GroundOccupancySnapshot(
        DateTimeOffset.UtcNow,
        OccupancyKnowledge.Available,
        new HashSet<string>(StringComparer.Ordinal) { "T:3" },
        new HashSet<uint>(),
        "test");
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:0", null, 0, 1, "Parking"),
        model.RunwayEnds.Single(item => item.Number == 21),
        occupied);
    Assert(route.Success, route.FailureReason);
    Assert(route.HoldShort?.NodeId == "T:4", $"attente={route.HoldShort?.NodeId}");
}

static void TestNoRoute()
{
    var model = BuildAirport();
    var blocked = new GroundOccupancySnapshot(
        DateTimeOffset.UtcNow,
        OccupancyKnowledge.Available,
        new HashSet<string>(StringComparer.Ordinal) { "T:3", "T:4" },
        new HashSet<uint>(),
        "test");
    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:0", null, 0, 1, "Parking"),
        model.RunwayEnds.Single(item => item.Number == 21),
        blocked);
    Assert(!route.Success);
    Assert(route.FailureReason.Contains("occup", StringComparison.OrdinalIgnoreCase));
}

static void TestUserAircraftExcludedFromOccupancy()
{
    var model = BuildAirport();
    var own = new GroundTrafficContact(
        0,
        "F-HNNY",
        46.0,
        LocalLongitude(46.0, -20),
        0,
        true,
        DateTimeOffset.UtcNow);
    var occupancy = GroundOccupancy.Build(
        model,
        new[] { own },
        DateTimeOffset.UtcNow,
        providerAvailable: true,
        userObjectId: 0);

    Assert(occupancy.Knowledge == OccupancyKnowledge.Available);
    Assert(occupancy.OccupiedNodeIds.Count == 0, $"nœuds occupés={occupancy.OccupiedNodeIds.Count}");
    Assert(occupancy.OccupiedEdgeIds.Count == 0, $"segments occupés={occupancy.OccupiedEdgeIds.Count}");
}

static void TestParkedNeighboursDoNotBlockLfbiRoute()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var now = DateTimeOffset.UtcNow;
    var contacts = new[]
    {
        ContactAtNode(model, "P:9", 109, 0, now),
        ContactAtNode(model, "P:10", 110, 0, now),
        ContactAtNode(model, "P:11", 111, 0, now),
    };
    var result = GroundOccupancy.BuildWithDiagnostics(model, contacts, now, true);

    Assert(result.Snapshot.OccupiedNodeIds
            .OrderBy(item => item, StringComparer.Ordinal)
            .SequenceEqual(new[] { "P:10", "P:11", "P:9" }),
        $"nœuds={string.Join(",", result.Snapshot.OccupiedNodeIds)}");
    Assert(result.Contacts.All(item => item.Classification == "PARKED_AT_STAND"));

    var route = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:12", null, 0, 1, "PARKING 6"),
        model.RunwayEnds.Single(item => item.Designator == "03"),
        result.Snapshot);
    Assert(route.Success, route.FailureReason);
}

static void TestTrafficOnTaxiwayBlocksNearestEdge()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var runway = model.RunwayEnds.Single(item => item.Designator == "03");
    var baseline = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:12", null, 0, 1, "PARKING 6"),
        runway,
        AvailableOccupancy());
    Assert(baseline.Success, baseline.FailureReason);
    var edge = baseline.Edges.First(item => item.Kind != TaxiPathKind.Parking);
    var from = model.Nodes[edge.FromNodeId];
    var to = model.Nodes[edge.ToNodeId];
    var contact = ContactAtLocal(
        model,
        (from.X + to.X) / 2.0,
        (from.Z + to.Z) / 2.0,
        501,
        8,
        DateTimeOffset.UtcNow);
    var result = GroundOccupancy.BuildWithDiagnostics(
        model,
        new[] { contact },
        contact.Timestamp,
        true);

    Assert(result.Snapshot.OccupiedEdgeIds.Count == 1,
        $"segments={string.Join(",", result.Snapshot.OccupiedEdgeIds)}");
    Assert(result.Snapshot.OccupiedEdgeIds.Contains(edge.SourceIndex));
    Assert(result.Contacts.Single().Classification == "MOVING_ON_NETWORK");
}


static void TestStationaryTrafficOnTaxiwayRemainsBlocking()
{
    var model = AirportGroundModelBuilder.Build(LoadFixture("LFBI-MSFS2024-ground.json"));
    var runway = model.RunwayEnds.Single(item => item.Designator == "03");
    var baseline = TaxiRouter.RouteToNearestAvailableHoldShort(
        model,
        new GroundLocation(GroundPositionKind.Parking, "P:12", null, 0, 1, "PARKING 6"),
        runway,
        AvailableOccupancy());
    Assert(baseline.Success, baseline.FailureReason);
    var edge = baseline.Edges.Last(item => item.Kind != TaxiPathKind.Parking);
    var from = model.Nodes[edge.FromNodeId];
    var to = model.Nodes[edge.ToNodeId];
    var contact = ContactAtLocal(
        model,
        (from.X + to.X) / 2.0,
        (from.Z + to.Z) / 2.0,
        502,
        0,
        DateTimeOffset.UtcNow);
    var result = GroundOccupancy.BuildWithDiagnostics(
        model,
        new[] { contact },
        contact.Timestamp,
        true);

    Assert(result.Snapshot.OccupiedEdgeIds.Contains(edge.SourceIndex));
    Assert(result.Contacts.Single().Classification == "STATIONARY_ON_NETWORK");
}

static GroundTrafficContact ContactAtNode(
    AirportGroundModel model,
    string nodeId,
    uint objectId,
    double speedKnots,
    DateTimeOffset timestamp)
{
    var node = model.Nodes[nodeId];
    return ContactAtLocal(model, node.X, node.Z, objectId, speedKnots, timestamp);
}

static GroundTrafficContact ContactAtLocal(
    AirportGroundModel model,
    double eastMeters,
    double northMeters,
    uint objectId,
    double speedKnots,
    DateTimeOffset timestamp)
{
    const double earthRadius = 6_371_000.0;
    var latitude = model.Latitude + (northMeters / earthRadius * 180.0 / Math.PI);
    var meanLatitude = (model.Latitude + latitude) / 2.0 * Math.PI / 180.0;
    var longitude = model.Longitude + (eastMeters / (earthRadius * Math.Cos(meanLatitude)) * 180.0 / Math.PI);
    return new GroundTrafficContact(
        objectId,
        $"AI-{objectId}",
        latitude,
        longitude,
        speedKnots,
        true,
        timestamp);
}

static void TestTakeoffFromParkingRefused()
{
    var engine = new GroundOperationsEngine();
    var decision = engine.Process(
        "demande décollage",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Unable);
    Assert(decision.ReasonCode == "TAKEOFF_INCOMPATIBLE");
}

static void TestFullCallsignInitialContact()
{
    var engine = new GroundOperationsEngine();
    var decision = engine.Process(
        "Poitiers Tour bonjour au parking pour tours de piste",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.SpokenText.StartsWith("Fox Hôtel Novembre Novembre Yankee", StringComparison.Ordinal));
    Assert(engine.Session.ContactEstablished);
}

static void TestShortCallsignAfterContact()
{
    var engine = new GroundOperationsEngine();
    _ = engine.Process(
        "Poitiers Tour bonjour au parking",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    var decision = engine.Process(
        "prêt au roulage",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Speak, decision.SystemMessage);
    Assert(decision.SpokenText.StartsWith("Fox Novembre Yankee", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.FullCallsign == "F-HNNY");
    Assert(decision.ShortCallsign == "F-NY");
    Assert(decision.SpokenText.Contains("roulez au point d'attente et rappelez prêt", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("Delta", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("via ", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
}

static void TestFirstTaxiMessageAndGreetingHistory()
{
    var engine = new GroundOperationsEngine();
    var decision = engine.Process(
        "Poitiers Tour de F-HNNY bonjour, au parking demande roulage",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Speak, decision.SystemMessage);
    Assert(decision.SpokenText.Contains("Poitiers Tour, bonjour", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.SpokenText.Contains("roulez au point d'attente", StringComparison.Ordinal), decision.SpokenText);
    Assert(engine.ContactHistory.Count == 1);
}

static void TestGreetingNotRepeatedOnSameStation()
{
    var engine = new GroundOperationsEngine();
    _ = engine.Process("Poitiers Tour bonjour au parking", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    var second = engine.Process("Poitiers Tour bonjour", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(!second.SpokenText.Contains("bonjour", StringComparison.OrdinalIgnoreCase), second.SpokenText);
    Assert(engine.ContactHistory.Single().GreetingCount == 1);
}

static void TestGroundToTowerNewContact()
{
    var engine = new GroundOperationsEngine();
    var ground = new RadioContext(ServiceCapability.Controlled, "Poitiers Sol", true, "test", "LFXX|GROUND", "GND", "LFXX", 121.700, "Local");
    var tower = ControlledRadio();
    _ = engine.Process("Poitiers Sol bonjour au parking", "F-HNNY", ground, BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    var towerContact = engine.Process("Poitiers Tour bonjour au parking", "F-HNNY", tower, BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(towerContact.SpokenText.Contains("Poitiers Tour, bonjour", StringComparison.Ordinal), towerContact.SpokenText);
    Assert(engine.ContactHistory.Count == 2);
}

static void TestAtisReturnKeepsTowerContact()
{
    var engine = new GroundOperationsEngine();
    var tower = ControlledRadio();
    _ = engine.Process("Poitiers Tour bonjour au parking", "F-HNNY", tower, BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    _ = engine.Process(
        "information Bravo",
        "F-HNNY",
        new RadioContext(ServiceCapability.AutomaticBroadcast, "Poitiers ATIS", false, "test", "LFXX|ATIS", "ATIS", "LFXX", 121.780, "Local"),
        BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    var back = engine.Process("Poitiers Tour de retour avec vous", "F-HNNY", tower, BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(back.SpokenText.Contains("rebonjour", StringComparison.OrdinalIgnoreCase), back.SpokenText);
    Assert(back.SpokenText.StartsWith("Fox Novembre Yankee", StringComparison.Ordinal), back.SpokenText);
}

static void TestReturnGreetingIntent()
{
    Assert(PilotIntentParser.Parse("Poitiers Tour de retour avec vous") == PilotIntent.InitialContact);
    Assert(PilotIntentParser.Parse("Poitiers Tour rebonjour") == PilotIntent.InitialContact);
    Assert(PilotIntentParser.Parse("Poitiers Tour de F-HNNY bonsoir") == PilotIntent.InitialContact);

    var evening = new GroundOperationsEngine().Process(
        "Poitiers Tour de F-HNNY bonsoir",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(evening.Action == ControllerAction.Speak, evening.SystemMessage);
    Assert(evening.ReasonCode == "INITIAL_CONTACT", evening.ReasonCode);
    Assert(evening.SpokenText.Contains("bonsoir", StringComparison.OrdinalIgnoreCase), evening.SpokenText);
}

static void TestGroundContextResetKeepsHistory()
{
    var engine = new GroundOperationsEngine();
    _ = engine.Process("Poitiers Tour bonjour au parking", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    engine.ResetGroundContext();
    var decision = engine.Process("Poitiers Tour bonjour", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(!decision.SpokenText.Contains("bonjour", StringComparison.OrdinalIgnoreCase), decision.SpokenText);
    Assert(engine.ContactHistory.Count == 1);
}

static void TestFlightSessionResetClearsHistory()
{
    var engine = new GroundOperationsEngine();
    _ = engine.Process("Poitiers Tour bonjour au parking", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    engine.ResetFlightSession();
    var decision = engine.Process("Poitiers Tour bonjour", "F-HNNY", ControlledRadio(), BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(decision.SpokenText.Contains("Poitiers Tour, bonjour", StringComparison.Ordinal), decision.SpokenText);
    Assert(engine.ContactHistory.Count == 1);
}

static void TestClearlyCalledOtherServiceSilent()
{
    var decision = new GroundOperationsEngine().Process(
        "Poitiers Sol de F-HNNY bonjour",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(decision.Action == ControllerAction.Silent, decision.ReasonCode);
    Assert(decision.ReasonCode == "CALLED_STATION_MISMATCH", decision.ReasonCode);
}

static void TestClearlyCalledOtherAirportSilent()
{
    var decision = new GroundOperationsEngine().Process(
        "Nantes Tour de F-HNNY bonjour",
        "F-HNNY",
        ControlledRadio(),
        BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(decision.Action == ControllerAction.Silent, decision.ReasonCode);
    Assert(decision.ReasonCode == "CALLED_STATION_MISMATCH", decision.ReasonCode);
}

static void TestRegionalFisRejectsGroundRequest()
{
    var fis = new RadioContext(ServiceCapability.InformationOnly, "Nantes Information", true, "test", "REGIONAL|NANTES|FIS", "FIS", string.Empty, 130.275, "Regional");
    var decision = new GroundOperationsEngine().Process(
        "Nantes Information bonjour demande roulage",
        "F-HNNY",
        fis,
        BuildAirport(), ParkingObservation(), AvailableOccupancy(), 210, 10);
    Assert(decision.Action == ControllerAction.Unable, decision.ReasonCode);
    Assert(decision.ReasonCode == "REGIONAL_FIS_GROUND_REQUEST", decision.ReasonCode);
}

static void TestAtisSilent()
{
    var decision = new GroundOperationsEngine().Process(
        "Poitiers information bonjour",
        "F-HNNY",
        new RadioContext(ServiceCapability.AutomaticBroadcast, "POITIERS ATIS", false, "test"),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Silent);
    Assert(string.IsNullOrEmpty(decision.SpokenText));
}

static void TestCtafSilent()
{
    var decision = new GroundOperationsEngine().Process(
        "Poitiers bonjour",
        "F-HNNY",
        new RadioContext(ServiceCapability.SelfInformation, "AUTO-INFORMATION", false, "test"),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Silent);
}

static void TestUnknownStationSilent()
{
    var decision = new GroundOperationsEngine().Process(
        "demande roulage",
        "F-HNNY",
        new RadioContext(ServiceCapability.Unknown, "INCONNUE", false, "test"),
        BuildAirport(),
        ParkingObservation(),
        AvailableOccupancy(),
        210,
        10);
    Assert(decision.Action == ControllerAction.Silent);
    Assert(decision.TaxiRoute is null);
}

static void TestNonRunwayGarbageIgnored()
{
    var snapshot = BuildSnapshot();
    var paths = snapshot.TaxiPaths
        .Select(path => path.Type == 1
            ? path with { RawRunwayNumber = 42_521_211, RawRunwayDesignator = -2_000_000_000 }
            : path)
        .ToArray();
    var model = AirportGroundModelBuilder.Build(snapshot with { TaxiPaths = paths });
    Assert(model.Edges.Where(edge => edge.Kind == TaxiPathKind.Taxi).All(edge => edge.RunwayNumber is null));
    Assert(model.Edges.Where(edge => edge.Kind == TaxiPathKind.Runway).All(edge => edge.RunwayNumber == 21));
}


static GroundOperationsEngine PrepareLfbiTaxiSession(
    AirportGroundModel model,
    AirportOperationalProfile profile)
{
    var engine = new GroundOperationsEngine();
    _ = engine.Process(
        "Poitiers Tour bonjour au parking",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    var taxi = engine.Process(
        "prêt au roulage",
        "F-HNNY",
        ControlledRadio(),
        model,
        ObservationAtNode(model, "P:12"),
        AvailableOccupancy(),
        210,
        10,
        profile,
        1015);
    Assert(taxi.ReasonCode == "TAXI_CLEARANCE_GENERIC_HOLD", taxi.ReasonCode);
    return engine;
}

static AirportOperationalProfile BuildLfbiProfile() => new(
    "LFBI",
    "2026-02-19",
    "VAC LFBI et phraséologie locale",
    "21",
    5,
    false,
    false,
    new[]
    {
        new OperationalPointDefinition(
            "LFBI-A3",
            "A3",
            OperationalPointRole.IntermediateHoldingPoint,
            46.5857814630,
            0.3088165993,
            new[] { "03", "21" },
            30),
        new OperationalPointDefinition(
            "LFBI-A2",
            "A2",
            OperationalPointRole.DepartureHoldingPoint,
            46.5863925087,
            0.3089542280,
            new[] { "03", "21" },
            30,
            DepartureHandling.IntersectionPreferred,
            "LFBI-A"),
        new OperationalPointDefinition(
            "LFBI-A",
            "A",
            OperationalPointRole.RunwayEntry,
            46.5871788329,
            0.3072940162,
            new[] { "03", "21" },
            45),
    });

static AircraftGroundObservation ObservationAtNode(
    AirportGroundModel model,
    string nodeId,
    double speedKnots = 0,
    double headingDegrees = 0)
{
    var node = model.Nodes[nodeId];
    const double earthRadius = 6_371_000.0;
    var latitude = model.Latitude + (node.Z / earthRadius * 180.0 / Math.PI);
    var meanLatitude = (model.Latitude + latitude) / 2.0 * Math.PI / 180.0;
    var longitude = model.Longitude + (node.X / (earthRadius * Math.Cos(meanLatitude)) * 180.0 / Math.PI);
    return new AircraftGroundObservation(
        DateTimeOffset.UtcNow,
        latitude,
        longitude,
        speedKnots,
        true,
        headingDegrees);
}

static AirportGroundModel BuildAirport() => AirportGroundModelBuilder.Build(BuildSnapshot());

static FacilityAirportSnapshot BuildSnapshot()
{
    var points = new[]
    {
        new FacilityTaxiPoint(0, 1, 0, 0, 0),
        new FacilityTaxiPoint(1, 1, 0, 100, 0),
        new FacilityTaxiPoint(2, 1, 0, 200, 0),
        new FacilityTaxiPoint(3, 5, 0, 300, -20),
        new FacilityTaxiPoint(4, 5, 0, 320, 45),
        new FacilityTaxiPoint(5, 1, 0, 335, -120),
        new FacilityTaxiPoint(6, 1, 0, 335, 120),
        new FacilityTaxiPoint(7, 1, 0, 260, 45),
    };
    var parking = new[]
    {
        new FacilityParking(0, 0, 0, 0, 0, 1, 0, 0, 15, -20, 0),
    };
    var names = new[]
    {
        new FacilityTaxiName(0, ""),
        new FacilityTaxiName(1, "D"),
        new FacilityTaxiName(2, "D1"),
        new FacilityTaxiName(3, "D2"),
    };
    var paths = new[]
    {
        new FacilityTaxiPath(0, 3, 20, 0, 0, 0, 0, 0, 1),
        new FacilityTaxiPath(1, 1, 20, 0, 0, 0, 0, 1, 1),
        new FacilityTaxiPath(2, 1, 20, 0, 0, 0, 1, 2, 1),
        new FacilityTaxiPath(3, 1, 20, 0, 0, 0, 2, 3, 2),
        new FacilityTaxiPath(4, 1, 20, 0, 0, 0, 2, 7, 3),
        new FacilityTaxiPath(5, 1, 20, 0, 0, 0, 7, 4, 3),
        new FacilityTaxiPath(6, 2, 45, 0, 21, 0, 5, 6, 0),
    };
    var runways = new[]
    {
        new FacilityRunway(0, 46.0, 0.0, 30, 2350, 45, 4, 3, 0, 21, 0),
    };
    return new FacilityAirportSnapshot(
        "LFXX",
        "Aérodrome test",
        46.0,
        0.0,
        runways,
        points,
        parking,
        paths,
        names);
}

static AircraftGroundObservation ParkingObservation()
{
    var latitude = 46.0;
    var longitude = LocalLongitude(46.0, -20);
    return new AircraftGroundObservation(DateTimeOffset.UtcNow, latitude, longitude, 0, true, 0);
}

static double LocalLongitude(double latitude, double eastMeters) =>
    eastMeters / (6_371_000.0 * Math.Cos(latitude * Math.PI / 180.0)) * 180.0 / Math.PI;

static GroundOccupancySnapshot AvailableOccupancy() => new(
    DateTimeOffset.UtcNow,
    OccupancyKnowledge.Available,
    new HashSet<string>(StringComparer.Ordinal),
    new HashSet<uint>(),
    "test");

static RadioContext ControlledRadio() =>
    new(ServiceCapability.Controlled, "Poitiers Tour", true, "test", "LFXX|TOWER", "TWR", "LFXX", 118.505, "Local");

static void Assert(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new InvalidOperationException(message ?? "Assertion échouée.");
    }
}

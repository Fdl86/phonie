using System.Text.Json;
using Phonie.Core;

var tests = new List<(string Name, Action Test)>
{
    ("graphe taxi valide", TestGraphBuild),
    ("capture LFBI MSFS 2024 exploitable", TestLfbiMsfs2024Fixture),
    ("tous parkings LFBI reliés à une attente", TestLfbiAllParkingsReachHold),
    ("capture LFBI MSFS 2020 normalisée", TestLfbiMsfs2020Fixture),
    ("chemin parking vers attente", TestParkingToHoldRoute),
    ("attente libre la plus proche", TestNearestAvailableHold),
    ("attente occupée exclue", TestOccupiedHoldExcluded),
    ("absence itinéraire signalée", TestNoRoute),
    ("avion utilisateur exclu de l'occupation", TestUserAircraftExcludedFromOccupancy),
    ("demande roulage reconnue", () => Assert(PilotIntentParser.Parse("demande roulage") == PilotIntent.TaxiRequest)),
    ("prêt au départ reconnu au point d'attente", () => Assert(PilotIntentParser.Parse("Fox Novembre Yankee prêt au départ") == PilotIntent.ReadyAtHoldShort)),
    ("alignement et décollage distinct", () => Assert(PilotIntentParser.Parse("demande alignement et décollage") == PilotIntent.LineUpAndTakeoffRequest)),
    ("décollage parking refusé", TestTakeoffFromParkingRefused),
    ("indicatif complet premier contact", TestFullCallsignInitialContact),
    ("indicatif abrégé après contact", TestShortCallsignAfterContact),
    ("ATIS sans dialogue", TestAtisSilent),
    ("CTAF sans dialogue", TestCtafSilent),
    ("station inconnue sans clairance", TestUnknownStationSilent),
    ("TaxiPath non-piste ignore piste corrompue", TestNonRunwayGarbageIgnored),
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
    Assert(route.HoldShort.AssociatedRunwayIndex == runway03.RunwayIndex);
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
    Assert(route.HoldShort.Label == "D1", $"attente={route.HoldShort.Label}");
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
    Assert(decision.SpokenText.Contains("point d'attente Delta un", StringComparison.Ordinal), decision.SpokenText);
    Assert(decision.SpokenText.Contains("via Delta", StringComparison.Ordinal), decision.SpokenText);
    Assert(!decision.SpokenText.Contains("via Delta et Delta un", StringComparison.Ordinal), decision.SpokenText);
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
    new(ServiceCapability.Controlled, "Poitiers Tour", true, "test");

static void Assert(bool condition, string? message = null)
{
    if (!condition)
    {
        throw new InvalidOperationException(message ?? "Assertion échouée.");
    }
}

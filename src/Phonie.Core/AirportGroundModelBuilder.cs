namespace Phonie.Core;

public static class AirportGroundModelBuilder
{
    private const double HoldShortRunwayAssociationLimitMeters = 180.0;

    public static AirportGroundModel Build(FacilityAirportSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var warnings = new List<string>();
        var names = snapshot.TaxiNames
            .GroupBy(item => item.Index)
            .ToDictionary(group => group.Key, group => CleanName(group.First().Name));

        var nodes = new Dictionary<string, GroundNode>(StringComparer.Ordinal);
        foreach (var point in snapshot.TaxiPoints)
        {
            var kind = IsHoldShortType(point.Type) ? GroundNodeKind.HoldShort : GroundNodeKind.TaxiPoint;
            var id = TaxiNodeId(point.Index);
            nodes[id] = new GroundNode(id, kind, point.Index, point.BiasX, point.BiasZ, string.Empty);
        }

        foreach (var parking in snapshot.Parkings)
        {
            var id = ParkingNodeId(parking.Index);
            nodes[id] = new GroundNode(
                id,
                GroundNodeKind.Parking,
                parking.Index,
                parking.BiasX,
                parking.BiasZ,
                FormatParkingLabel(parking));
        }

        var edges = new List<GroundEdge>();
        foreach (var path in snapshot.TaxiPaths.OrderBy(item => item.Index))
        {
            var kind = NormalizePathKind(path.Type);
            if (!TryResolveEndpoints(path, kind, nodes, out var fromNodeId, out var toNodeId))
            {
                warnings.Add($"TaxiPath #{path.Index} ignoré : extrémités {path.StartIndex}/{path.EndIndex} introuvables.");
                continue;
            }

            var from = nodes[fromNodeId];
            var to = nodes[toNodeId];
            var runwayNumber = kind == TaxiPathKind.Runway && path.RawRunwayNumber is >= 1 and <= 36
                ? path.RawRunwayNumber
                : null;
            var runwayDesignator = runwayNumber.HasValue && path.RawRunwayDesignator is >= 0 and <= 7
                ? path.RawRunwayDesignator
                : null;

            // MSFS 2020 peut fournir des octets indéfinis dans les champs piste des
            // chemins non-piste. Ils sont conservés dans le rapport brut mais ne
            // doivent jamais contaminer le modèle opérationnel.
            var closed = kind == TaxiPathKind.Closed;
            var traversable = kind is TaxiPathKind.Taxi or TaxiPathKind.Parking or TaxiPathKind.Path;
            var taxiwayName = names.TryGetValue(path.NameIndex, out var name) ? name : string.Empty;
            var length = Geometry.Distance(from.X, from.Z, to.X, to.Z);
            if (!double.IsFinite(length) || length < 0.1)
            {
                warnings.Add($"TaxiPath #{path.Index} ignoré : longueur nulle ou invalide.");
                continue;
            }

            edges.Add(new GroundEdge(
                path.Index,
                fromNodeId,
                toNodeId,
                kind,
                taxiwayName,
                length,
                path.WidthMeters,
                path.WeightLimit,
                runwayNumber,
                runwayDesignator,
                traversable,
                kind == TaxiPathKind.Runway,
                closed));
        }

        var runwayEnds = BuildRunwayEnds(snapshot.Runways);
        var holdShortPoints = BuildHoldShortPoints(nodes, edges, runwayEnds);
        if (holdShortPoints.Count == 0)
        {
            warnings.Add("Aucun point d'attente exploitable détecté dans les données Facilities.");
        }

        return new AirportGroundModel(
            snapshot.Icao.Trim().ToUpperInvariant(),
            snapshot.Name.Trim(),
            snapshot.Latitude,
            snapshot.Longitude,
            nodes,
            edges,
            runwayEnds,
            holdShortPoints,
            warnings);
    }

    private static IReadOnlyList<RunwayEnd> BuildRunwayEnds(IReadOnlyList<FacilityRunway> runways)
    {
        var result = new List<RunwayEnd>();
        foreach (var runway in runways)
        {
            if (runway.LengthMeters < 300 || runway.PrimaryNumber is < 1 or > 36 || runway.SecondaryNumber is < 1 or > 36)
            {
                continue;
            }

            result.Add(new RunwayEnd(
                FormatRunway(runway.PrimaryNumber, runway.PrimaryDesignator),
                runway.PrimaryNumber,
                runway.PrimaryDesignator,
                Geometry.NormalizeHeading(runway.HeadingDegrees),
                runway.Index,
                runway.LengthMeters));
            result.Add(new RunwayEnd(
                FormatRunway(runway.SecondaryNumber, runway.SecondaryDesignator),
                runway.SecondaryNumber,
                runway.SecondaryDesignator,
                Geometry.NormalizeHeading(runway.HeadingDegrees + 180.0),
                runway.Index,
                runway.LengthMeters));
        }

        return result;
    }

    private static IReadOnlyList<HoldShortPoint> BuildHoldShortPoints(
        IReadOnlyDictionary<string, GroundNode> nodes,
        IReadOnlyList<GroundEdge> edges,
        IReadOnlyList<RunwayEnd> runwayEnds)
    {
        var runwayEdges = edges.Where(edge => edge.IsRunway && edge.RunwayNumber.HasValue).ToArray();
        var result = new List<HoldShortPoint>();

        foreach (var node in nodes.Values.Where(item => item.Kind == GroundNodeKind.HoldShort))
        {
            GroundEdge? nearestRunway = null;
            var nearestDistance = double.PositiveInfinity;
            foreach (var runwayEdge in runwayEdges)
            {
                if (!nodes.TryGetValue(runwayEdge.FromNodeId, out var from)
                    || !nodes.TryGetValue(runwayEdge.ToNodeId, out var to))
                {
                    continue;
                }

                var distance = Geometry.DistancePointToSegment(node.X, node.Z, from.X, from.Z, to.X, to.Z);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestRunway = runwayEdge;
                }
            }

            if (nearestRunway is null || nearestDistance > HoldShortRunwayAssociationLimitMeters)
            {
                continue;
            }

            var incidentNames = edges
                .Where(edge => edge.FromNodeId == node.Id || edge.ToNodeId == node.Id)
                .Select(edge => edge.TaxiwayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(name => name.Length)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var label = !string.IsNullOrWhiteSpace(nearestRunway.TaxiwayName)
                ? nearestRunway.TaxiwayName
                : incidentNames.FirstOrDefault() ?? string.Empty;

            var physicalRunway = runwayEnds.FirstOrDefault(item => item.Number == nearestRunway.RunwayNumber);
            result.Add(new HoldShortPoint(
                node.Id,
                label,
                physicalRunway?.RunwayIndex,
                nearestRunway.RunwayNumber,
                nearestDistance));
        }

        return result
            .OrderBy(item => item.AssociatedRunwayIndex)
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.NodeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryResolveEndpoints(
        FacilityTaxiPath path,
        TaxiPathKind kind,
        IReadOnlyDictionary<string, GroundNode> nodes,
        out string fromNodeId,
        out string toNodeId)
    {
        fromNodeId = TaxiNodeId(path.StartIndex);
        toNodeId = TaxiNodeId(path.EndIndex);

        if (kind == TaxiPathKind.Parking)
        {
            var standardFrom = TaxiNodeId(path.StartIndex);
            var standardTo = ParkingNodeId(path.EndIndex);
            if (nodes.ContainsKey(standardFrom) && nodes.ContainsKey(standardTo))
            {
                fromNodeId = standardFrom;
                toNodeId = standardTo;
                return true;
            }

            var reversedFrom = ParkingNodeId(path.StartIndex);
            var reversedTo = TaxiNodeId(path.EndIndex);
            if (nodes.ContainsKey(reversedFrom) && nodes.ContainsKey(reversedTo))
            {
                fromNodeId = reversedFrom;
                toNodeId = reversedTo;
                return true;
            }

            return false;
        }

        return nodes.ContainsKey(fromNodeId) && nodes.ContainsKey(toNodeId);
    }

    private static TaxiPathKind NormalizePathKind(int rawType) =>
        Enum.IsDefined(typeof(TaxiPathKind), rawType)
            ? (TaxiPathKind)rawType
            : TaxiPathKind.Unknown;

    private static bool IsHoldShortType(int rawType) => rawType is 4 or 5 or 6 or 7;

    private static string TaxiNodeId(int index) => $"T:{index}";

    private static string TaxiNodeId(uint index) => $"T:{index}";

    private static string ParkingNodeId(int index) => $"P:{index}";

    private static string ParkingNodeId(uint index) => $"P:{index}";

    private static string CleanName(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string FormatParkingLabel(FacilityParking parking) =>
        parking.Number > 0 ? $"PARKING {parking.Number}" : $"PARKING {parking.Index}";

    public static string FormatRunway(int number, int designator)
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
        return number is >= 1 and <= 36 ? $"{number:00}{suffix}" : "-";
    }
}

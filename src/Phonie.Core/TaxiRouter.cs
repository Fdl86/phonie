namespace Phonie.Core;

public static class TaxiRouter
{
    public static TaxiRoute RouteToNearestAvailableHoldShort(
        AirportGroundModel model,
        GroundLocation start,
        RunwayEnd runway,
        GroundOccupancySnapshot occupancy) =>
        RouteToNearestAvailableHoldShort(model, start, runway, occupancy, null);

    public static TaxiRoute RouteToNearestAvailableHoldShort(
        AirportGroundModel model,
        GroundLocation start,
        RunwayEnd runway,
        GroundOccupancySnapshot occupancy,
        AirportOperationalProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(runway);
        ArgumentNullException.ThrowIfNull(occupancy);

        var startNodeId = ResolveStartNode(model, start);
        if (startNodeId is null)
        {
            return Failure("Position de départ non raccordée à un nœud taxi.");
        }

        var resolutions = OperationalPointResolver.Resolve(model, profile);
        // Every genuine Facilities HOLD_SHORT is operationally acceptable. When the scenery
        // exposes a reliable runway association, those points are preferred. If the association
        // metadata is missing or corrupt, PHONIE falls back to all genuine HOLD_SHORT nodes.
        var associatedHolds = model.HoldShortPoints
            .Where(item => item.AssociatedRunwayIndex == runway.RunwayIndex)
            .ToArray();
        var routingHolds = associatedHolds.Length > 0
            ? associatedHolds
            : model.HoldShortPoints.ToArray();
        var allCandidates = routingHolds
            .Where(item => !occupancy.OccupiedNodeIds.Contains(item.NodeId))
            .Select(item => new
            {
                Hold = item,
                Operational = resolutions.GetValueOrDefault(item.NodeId),
            })
            .ToArray();

        if (allCandidates.Length == 0)
        {
            if (routingHolds.Length == 0)
            {
                return Failure("Aucun vrai point d'attente Facilities détecté sur cet aérodrome.");
            }

            return Failure("Tous les points d'attente détectés sont occupés.");
        }

        TaxiRoute? best = null;
        foreach (var candidate in allCandidates)
        {
            var route = FindRoute(
                model,
                startNodeId,
                candidate.Hold.NodeId,
                runway,
                candidate.Hold,
                candidate.Operational,
                occupancy,
                profile);
            if (route.Success && (best is null || route.TotalDistanceMeters < best.TotalDistanceMeters))
            {
                best = route;
            }
        }

        return best ?? Failure("Aucun itinéraire accessible vers un point d'attente libre.");

        TaxiRoute Failure(string reason) => new(
            false,
            reason,
            startNodeId ?? string.Empty,
            null,
            runway,
            Array.Empty<GroundEdge>(),
            Array.Empty<string>(),
            0,
            0);
    }

    private static TaxiRoute FindRoute(
        AirportGroundModel model,
        string startNodeId,
        string targetNodeId,
        RunwayEnd runway,
        HoldShortPoint hold,
        OperationalPointResolution? operationalPoint,
        GroundOccupancySnapshot occupancy,
        AirportOperationalProfile? profile)
    {
        var adjacency = new Dictionary<string, List<GroundEdge>>(StringComparer.Ordinal);
        foreach (var edge in model.Edges)
        {
            if (!edge.Traversable || edge.IsClosed || occupancy.OccupiedEdgeIds.Contains(edge.SourceIndex))
            {
                continue;
            }

            Add(edge.FromNodeId, edge);
            Add(edge.ToNodeId, edge);
        }

        var distances = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [startNodeId] = 0,
        };
        var previousNode = new Dictionary<string, string>(StringComparer.Ordinal);
        var previousEdge = new Dictionary<string, GroundEdge>(StringComparer.Ordinal);
        var queue = new PriorityQueue<string, double>();
        queue.Enqueue(startNodeId, 0);

        while (queue.TryDequeue(out var nodeId, out var distance))
        {
            if (distance > distances.GetValueOrDefault(nodeId, double.PositiveInfinity))
            {
                continue;
            }

            if (string.Equals(nodeId, targetNodeId, StringComparison.Ordinal))
            {
                break;
            }

            if (!adjacency.TryGetValue(nodeId, out var incident))
            {
                continue;
            }

            foreach (var edge in incident)
            {
                var next = edge.FromNodeId == nodeId ? edge.ToNodeId : edge.FromNodeId;
                if (occupancy.OccupiedNodeIds.Contains(next) && next != targetNodeId)
                {
                    continue;
                }

                var penalty = string.IsNullOrWhiteSpace(edge.TaxiwayName) ? 20.0 : 0.0;
                var candidateDistance = distance + edge.LengthMeters + penalty;
                if (candidateDistance >= distances.GetValueOrDefault(next, double.PositiveInfinity))
                {
                    continue;
                }

                distances[next] = candidateDistance;
                previousNode[next] = nodeId;
                previousEdge[next] = edge;
                queue.Enqueue(next, candidateDistance);
            }
        }

        if (!distances.ContainsKey(targetNodeId))
        {
            return new TaxiRoute(
                false,
                "Point d'attente inaccessible.",
                startNodeId,
                hold,
                runway,
                Array.Empty<GroundEdge>(),
                Array.Empty<string>(),
                0,
                0,
                operationalPoint);
        }

        var reversed = new List<GroundEdge>();
        var cursor = targetNodeId;
        while (!string.Equals(cursor, startNodeId, StringComparison.Ordinal))
        {
            if (!previousNode.TryGetValue(cursor, out var parent) || !previousEdge.TryGetValue(cursor, out var edge))
            {
                return new TaxiRoute(
                    false,
                    "Reconstruction de l'itinéraire impossible.",
                    startNodeId,
                    hold,
                    runway,
                    Array.Empty<GroundEdge>(),
                    Array.Empty<string>(),
                    0,
                    0,
                    operationalPoint);
            }

            reversed.Add(edge);
            cursor = parent;
        }

        reversed.Reverse();
        var names = CollapseTaxiwayNames(reversed);
        var radioLabel = operationalPoint?.RadioLabel ?? hold.Label;
        if (names.Count > 0
            && !string.IsNullOrWhiteSpace(radioLabel)
            && string.Equals(names[^1], radioLabel, StringComparison.OrdinalIgnoreCase))
        {
            names = names.Take(names.Count - 1).ToArray();
        }

        var total = reversed.Sum(item => item.LengthMeters);
        var confidence = Math.Clamp(
            Math.Min(1.0, hold.DistanceToRunwayMeters <= 100 ? 0.95 : 0.8)
            * (occupancy.Knowledge == OccupancyKnowledge.Available ? 1.0 : 0.75)
            * ((operationalPoint?.Confidence ?? OperationalLabelConfidence.Low) >= OperationalLabelConfidence.Medium ? 1.0 : 0.8),
            0,
            1);
        var runwayEntry = OperationalPointResolver.ResolveRunwayEntry(model, profile, operationalPoint, runway);
        var includeVia = profile?.SpeakViaTaxiways ?? names.Count is > 0 and <= 2;

        return new TaxiRoute(
            true,
            string.Empty,
            startNodeId,
            hold,
            runway,
            reversed,
            names,
            total,
            confidence,
            operationalPoint,
            runwayEntry,
            includeVia);

        void Add(string nodeId, GroundEdge edge)
        {
            if (!adjacency.TryGetValue(nodeId, out var list))
            {
                list = new List<GroundEdge>();
                adjacency[nodeId] = list;
            }

            list.Add(edge);
        }
    }

    public static string BuildDiagnostic(
        AirportGroundModel model,
        GroundLocation start,
        RunwayEnd runway,
        GroundOccupancySnapshot occupancy,
        AirportOperationalProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(runway);
        ArgumentNullException.ThrowIfNull(occupancy);

        var startNodeId = ResolveStartNode(model, start);
        if (startNodeId is null)
        {
            return "Départ : aucun nœud taxi raccordé à la position avion.";
        }

        var resolutions = OperationalPointResolver.Resolve(model, profile);
        var lines = new List<string>
        {
            $"Départ : {startNodeId} - {start.Description}",
            $"Piste analysée : {runway.Designator}",
            $"Occupation appliquée : {occupancy.OccupiedNodeIds.Count} nœud(s), {occupancy.OccupiedEdgeIds.Count} segment(s).",
            OperationalPointResolver.BuildDiagnostic(model, profile, resolutions),
        };

        var emptyOccupancy = new GroundOccupancySnapshot(
            occupancy.Timestamp,
            OccupancyKnowledge.Available,
            new HashSet<string>(StringComparer.Ordinal),
            new HashSet<uint>(),
            "Diagnostic sans occupation.");
        var candidates = model.HoldShortPoints
            .Where(item => item.AssociatedRunwayIndex == runway.RunwayIndex)
            .OrderBy(item => item.NodeId, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            lines.Add("Candidats : aucun point d'attente associé à cette piste.");
            return string.Join(Environment.NewLine, lines);
        }

        foreach (var candidate in candidates)
        {
            var resolution = resolutions.GetValueOrDefault(candidate.NodeId);
            var displayLabel = resolution?.HasReliableRadioLabel == true
                ? resolution.RadioLabel
                : "point générique";
            if (occupancy.OccupiedNodeIds.Contains(candidate.NodeId))
            {
                lines.Add($"Candidat {displayLabel} ({candidate.NodeId}) : rejeté, point d'attente occupé.");
                continue;
            }

            var route = FindRoute(model, startNodeId, candidate.NodeId, runway, candidate, resolution, occupancy, profile);
            if (route.Success)
            {
                var via = route.TaxiwayNames.Count == 0
                    ? "sans nom de voie"
                    : string.Join(" - ", route.TaxiwayNames);
                lines.Add(
                    $"Candidat {displayLabel} ({candidate.NodeId}) : ACCESSIBLE, {route.TotalDistanceMeters:F0} m, via {via}, " +
                    $"rôle {resolution?.Role}, confiance {resolution?.Confidence}.");
                continue;
            }

            var baseline = FindRoute(model, startNodeId, candidate.NodeId, runway, candidate, resolution, emptyOccupancy, profile);
            if (!baseline.Success)
            {
                lines.Add($"Candidat {displayLabel} ({candidate.NodeId}) : réseau de base déconnecté.");
                continue;
            }

            var blockedEdges = baseline.Edges
                .Where(edge => occupancy.OccupiedEdgeIds.Contains(edge.SourceIndex))
                .Select(edge => edge.SourceIndex)
                .Distinct()
                .OrderBy(item => item)
                .ToArray();
            var baselineNodes = baseline.Edges
                .SelectMany(edge => new[] { edge.FromNodeId, edge.ToNodeId })
                .Distinct(StringComparer.Ordinal)
                .Where(nodeId => occupancy.OccupiedNodeIds.Contains(nodeId))
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            var blockers = new List<string>();
            if (blockedEdges.Length > 0)
            {
                blockers.Add($"segments {string.Join(",", blockedEdges)}");
            }

            if (baselineNodes.Length > 0)
            {
                blockers.Add($"nœuds {string.Join(",", baselineNodes)}");
            }

            lines.Add(
                blockers.Count == 0
                    ? $"Candidat {displayLabel} ({candidate.NodeId}) : inaccessible malgré un chemin de base ; diagnostic approfondi requis."
                    : $"Candidat {displayLabel} ({candidate.NodeId}) : chemin de base {baseline.TotalDistanceMeters:F0} m bloqué par {string.Join(" et ", blockers)}.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string? ResolveStartNode(AirportGroundModel model, GroundLocation location)
    {
        if (!string.IsNullOrWhiteSpace(location.NodeId) && model.Nodes.ContainsKey(location.NodeId))
        {
            return location.NodeId;
        }

        if (location.EdgeId.HasValue)
        {
            var edge = model.Edges.FirstOrDefault(item => item.SourceIndex == location.EdgeId.Value);
            if (edge is not null)
            {
                var from = model.Nodes[edge.FromNodeId];
                var to = model.Nodes[edge.ToNodeId];
                return location.DistanceMeters <= edge.LengthMeters / 2.0 ? from.Id : to.Id;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> CollapseTaxiwayNames(IReadOnlyList<GroundEdge> edges)
    {
        var result = new List<string>();
        foreach (var edge in edges)
        {
            var name = edge.TaxiwayName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (result.Count == 0 || !string.Equals(result[^1], name, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(name);
            }
        }

        return result;
    }
}

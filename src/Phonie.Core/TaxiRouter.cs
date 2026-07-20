namespace Phonie.Core;

public static class TaxiRouter
{
    public static TaxiRoute RouteToNearestAvailableHoldShort(
        AirportGroundModel model,
        GroundLocation start,
        RunwayEnd runway,
        GroundOccupancySnapshot occupancy)
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

        var candidates = model.HoldShortPoints
            .Where(item => item.AssociatedRunwayIndex == runway.RunwayIndex)
            .Where(item => !string.IsNullOrWhiteSpace(item.Label))
            .Where(item => !occupancy.OccupiedNodeIds.Contains(item.NodeId))
            .ToArray();

        if (candidates.Length == 0)
        {
            var runwayHolds = model.HoldShortPoints
                .Where(item => item.AssociatedRunwayIndex == runway.RunwayIndex)
                .ToArray();
            if (runwayHolds.Length == 0)
            {
                return Failure($"Aucun point d'attente associé à la piste {runway.Designator}.");
            }

            if (runwayHolds.All(item => string.IsNullOrWhiteSpace(item.Label)))
            {
                return Failure("Aucun point d'attente ne possède un nom radio fiable.");
            }

            return Failure("Tous les points d'attente nommés associés sont occupés.");
        }

        TaxiRoute? best = null;
        foreach (var candidate in candidates)
        {
            var route = FindRoute(model, startNodeId, candidate.NodeId, runway, candidate, occupancy);
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
        GroundOccupancySnapshot occupancy)
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
                0);
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
                    0);
            }

            reversed.Add(edge);
            cursor = parent;
        }

        reversed.Reverse();
        var names = CollapseTaxiwayNames(reversed);
        if (names.Count > 0
            && !string.IsNullOrWhiteSpace(hold.Label)
            && string.Equals(names[^1], hold.Label, StringComparison.OrdinalIgnoreCase))
        {
            // Le dernier embranchement nommé comme le point d'attente est annoncé
            // dans « point d'attente D1 » et n'est pas répété dans « via Delta ».
            names = names.Take(names.Count - 1).ToArray();
        }

        var total = reversed.Sum(item => item.LengthMeters);
        var confidence = Math.Clamp(
            Math.Min(1.0, hold.DistanceToRunwayMeters <= 100 ? 0.95 : 0.8)
            * (occupancy.Knowledge == OccupancyKnowledge.Available ? 1.0 : 0.75),
            0,
            1);

        return new TaxiRoute(
            true,
            string.Empty,
            startNodeId,
            hold,
            runway,
            reversed,
            names,
            total,
            confidence);

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
                return edge.FromNodeId;
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

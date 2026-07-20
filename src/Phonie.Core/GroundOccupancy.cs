namespace Phonie.Core;

public static class GroundOccupancy
{
    private const double StationarySpeedThresholdKnots = 2.0;
    private const double ParkingAssociationRadiusMeters = 30.0;
    private const double ParkingVsTaxiwayToleranceMeters = 10.0;
    private const double EndpointOccupancyRadiusMeters = 8.0;
    private const double HoldShortOccupancyRadiusMeters = 12.0;
    private static readonly TimeSpan MaximumTrafficAge = TimeSpan.FromSeconds(5);

    public static GroundOccupancySnapshot Build(
        AirportGroundModel model,
        IReadOnlyList<GroundTrafficContact> contacts,
        DateTimeOffset now,
        bool providerAvailable,
        uint? userObjectId = null) =>
        BuildWithDiagnostics(model, contacts, now, providerAvailable, userObjectId).Snapshot;

    public static GroundOccupancyBuildResult BuildWithDiagnostics(
        AirportGroundModel model,
        IReadOnlyList<GroundTrafficContact> contacts,
        DateTimeOffset now,
        bool providerAvailable,
        uint? userObjectId = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(contacts);

        if (!providerAvailable)
        {
            return new GroundOccupancyBuildResult(
                GroundOccupancySnapshot.Unknown(now, "Trafic SimConnect indisponible."),
                Array.Empty<GroundTrafficOccupancyDiagnostic>());
        }

        var occupiedNodes = new HashSet<string>(StringComparer.Ordinal);
        var occupiedEdges = new HashSet<uint>();
        var diagnostics = new List<GroundTrafficOccupancyDiagnostic>(contacts.Count);
        var parkingNodes = model.Nodes.Values
            .Where(node => node.Kind == GroundNodeKind.Parking)
            .ToArray();
        var usableEdges = model.Edges
            .Where(edge => edge.Traversable || edge.IsRunway)
            .ToArray();

        foreach (var contact in contacts)
        {
            if (userObjectId.HasValue && contact.ObjectId == userObjectId.Value)
            {
                diagnostics.Add(Ignored(contact, "IGNORED_USER", "Avion utilisateur exclu explicitement."));
                continue;
            }

            if (!contact.IsOnGround)
            {
                diagnostics.Add(Ignored(contact, "IGNORED_AIRBORNE", "Objet non posé au sol."));
                continue;
            }

            if (now - contact.Timestamp > MaximumTrafficAge)
            {
                diagnostics.Add(Ignored(contact, "IGNORED_STALE", "Position trafic trop ancienne."));
                continue;
            }

            if (!double.IsFinite(contact.Latitude)
                || !double.IsFinite(contact.Longitude)
                || !double.IsFinite(contact.GroundSpeedKnots))
            {
                diagnostics.Add(Ignored(contact, "IGNORED_INVALID", "Coordonnées ou vitesse non valides."));
                continue;
            }

            var (x, z) = Geometry.ProjectLocal(model.Latitude, model.Longitude, contact.Latitude, contact.Longitude);
            var nearestParking = parkingNodes
                .Select(node => new
                {
                    Node = node,
                    Distance = Geometry.Distance(x, z, node.X, node.Z),
                })
                .OrderBy(item => item.Distance)
                .FirstOrDefault();
            var nearestEdge = usableEdges
                .Select(edge =>
                {
                    if (!model.Nodes.TryGetValue(edge.FromNodeId, out var from)
                        || !model.Nodes.TryGetValue(edge.ToNodeId, out var to))
                    {
                        return null;
                    }

                    return new
                    {
                        Edge = edge,
                        From = from,
                        To = to,
                        Distance = Geometry.DistancePointToSegment(x, z, from.X, from.Z, to.X, to.Z),
                    };
                })
                .Where(item => item is not null)
                .OrderBy(item => item!.Distance)
                .FirstOrDefault();

            var contactNodes = new HashSet<string>(StringComparer.Ordinal);
            var contactEdges = new HashSet<uint>();
            var stationary = contact.GroundSpeedKnots <= StationarySpeedThresholdKnots;

            var parkedAtStand = stationary
                && nearestParking is not null
                && nearestParking.Distance <= ParkingAssociationRadiusMeters
                && (nearestEdge is null
                    || nearestEdge.Edge.Kind == TaxiPathKind.Parking
                    || nearestParking.Distance <= 12.0
                    || nearestParking.Distance <= nearestEdge.Distance + ParkingVsTaxiwayToleranceMeters);

            if (parkedAtStand)
            {
                contactNodes.Add(nearestParking.Node.Id);
                foreach (var edge in model.Edges.Where(edge =>
                             edge.Kind == TaxiPathKind.Parking
                             && (string.Equals(edge.FromNodeId, nearestParking.Node.Id, StringComparison.Ordinal)
                                 || string.Equals(edge.ToNodeId, nearestParking.Node.Id, StringComparison.Ordinal))))
                {
                    contactEdges.Add(edge.SourceIndex);
                }

                Merge(contactNodes, contactEdges);
                diagnostics.Add(new GroundTrafficOccupancyDiagnostic(
                    contact.ObjectId,
                    contact.Callsign,
                    contact.GroundSpeedKnots,
                    contact.IsOnGround,
                    "PARKED_AT_STAND",
                    nearestParking.Node.Id,
                    nearestParking.Distance,
                    nearestEdge?.Edge.SourceIndex,
                    nearestEdge?.Distance,
                    contactNodes.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                    contactEdges.OrderBy(item => item).ToArray(),
                    "Objet immobile associé à un parking : seul le parking et sa bretelle directe sont bloqués, jamais le taxiway commun voisin."));
                continue;
            }

            if (nearestEdge is null)
            {
                diagnostics.Add(new GroundTrafficOccupancyDiagnostic(
                    contact.ObjectId,
                    contact.Callsign,
                    contact.GroundSpeedKnots,
                    contact.IsOnGround,
                    "OUTSIDE_NETWORK",
                    nearestParking?.Node.Id,
                    nearestParking?.Distance,
                    null,
                    null,
                    Array.Empty<string>(),
                    Array.Empty<uint>(),
                    "Aucun segment exploitable à proximité."));
                continue;
            }

            var corridorRadius = Math.Clamp(nearestEdge.Edge.WidthMeters / 2.0 + 3.0, 8.0, 18.0);
            if (nearestEdge.Distance > corridorRadius)
            {
                diagnostics.Add(new GroundTrafficOccupancyDiagnostic(
                    contact.ObjectId,
                    contact.Callsign,
                    contact.GroundSpeedKnots,
                    contact.IsOnGround,
                    "OUTSIDE_NETWORK",
                    nearestParking?.Node.Id,
                    nearestParking?.Distance,
                    nearestEdge.Edge.SourceIndex,
                    nearestEdge.Distance,
                    Array.Empty<string>(),
                    Array.Empty<uint>(),
                    $"Objet à {nearestEdge.Distance:F1} m du segment le plus proche, hors corridor utile de {corridorRadius:F1} m."));
                continue;
            }

            contactEdges.Add(nearestEdge.Edge.SourceIndex);
            if (Geometry.Distance(x, z, nearestEdge.From.X, nearestEdge.From.Z) <= EndpointOccupancyRadiusMeters)
            {
                contactNodes.Add(nearestEdge.From.Id);
            }

            if (Geometry.Distance(x, z, nearestEdge.To.X, nearestEdge.To.Z) <= EndpointOccupancyRadiusMeters)
            {
                contactNodes.Add(nearestEdge.To.Id);
            }

            foreach (var hold in model.HoldShortPoints)
            {
                if (model.Nodes.TryGetValue(hold.NodeId, out var holdNode)
                    && Geometry.Distance(x, z, holdNode.X, holdNode.Z) <= HoldShortOccupancyRadiusMeters)
                {
                    contactNodes.Add(hold.NodeId);
                }
            }

            Merge(contactNodes, contactEdges);
            diagnostics.Add(new GroundTrafficOccupancyDiagnostic(
                contact.ObjectId,
                contact.Callsign,
                contact.GroundSpeedKnots,
                contact.IsOnGround,
                stationary ? "STATIONARY_ON_NETWORK" : "MOVING_ON_NETWORK",
                nearestParking?.Node.Id,
                nearestParking?.Distance,
                nearestEdge.Edge.SourceIndex,
                nearestEdge.Distance,
                contactNodes.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                contactEdges.OrderBy(item => item).ToArray(),
                $"Objet réellement dans le corridor du segment {nearestEdge.Edge.SourceIndex} à {nearestEdge.Distance:F1} m ; corridor {corridorRadius:F1} m."));
        }

        var source = diagnostics.Count == 0
            ? "Aucun objet avion SimConnect pertinent sur le réseau."
            : $"{diagnostics.Count} objet(s) analysé(s) avec occupation géométrique ciblée.";
        var snapshot = new GroundOccupancySnapshot(
            now,
            OccupancyKnowledge.Available,
            occupiedNodes,
            occupiedEdges,
            source);
        return new GroundOccupancyBuildResult(snapshot, diagnostics);

        void Merge(IEnumerable<string> nodes, IEnumerable<uint> edges)
        {
            occupiedNodes.UnionWith(nodes);
            occupiedEdges.UnionWith(edges);
        }
    }

    private static GroundTrafficOccupancyDiagnostic Ignored(
        GroundTrafficContact contact,
        string classification,
        string reason) =>
        new(
            contact.ObjectId,
            contact.Callsign,
            contact.GroundSpeedKnots,
            contact.IsOnGround,
            classification,
            null,
            null,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<uint>(),
            reason);
}

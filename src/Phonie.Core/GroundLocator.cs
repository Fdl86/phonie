namespace Phonie.Core;

public static class GroundLocator
{
    private const double ParkingSnapMeters = 45.0;
    private const double HoldShortSnapMeters = 35.0;
    private const double TaxiwaySnapMeters = 60.0;
    private const double RunwaySnapMeters = 70.0;

    public static GroundLocation Locate(AirportGroundModel model, AircraftGroundObservation observation)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(observation);

        if (!observation.IsOnGround)
        {
            return new GroundLocation(GroundPositionKind.Airborne, null, null, 0, 1, "Avion en vol.");
        }

        var (x, z) = Geometry.ProjectLocal(model.Latitude, model.Longitude, observation.Latitude, observation.Longitude);

        var nearestParking = model.Nodes.Values
            .Where(node => node.Kind == GroundNodeKind.Parking)
            .Select(node => (Node: node, Distance: Geometry.Distance(x, z, node.X, node.Z)))
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (nearestParking.Node is not null && nearestParking.Distance <= ParkingSnapMeters)
        {
            return new GroundLocation(
                GroundPositionKind.Parking,
                nearestParking.Node.Id,
                null,
                nearestParking.Distance,
                Confidence(nearestParking.Distance, ParkingSnapMeters),
                nearestParking.Node.Label);
        }

        var nearestHold = model.HoldShortPoints
            .Where(hold => model.Nodes.ContainsKey(hold.NodeId))
            .Select(hold => (Hold: hold, Node: model.Nodes[hold.NodeId]))
            .Select(item => (item.Hold, item.Node, Distance: Geometry.Distance(x, z, item.Node.X, item.Node.Z)))
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (nearestHold.Node is not null && nearestHold.Distance <= HoldShortSnapMeters)
        {
            return new GroundLocation(
                GroundPositionKind.HoldShort,
                nearestHold.Node.Id,
                null,
                nearestHold.Distance,
                Confidence(nearestHold.Distance, HoldShortSnapMeters),
                $"Point d'attente {nearestHold.Hold.Label}");
        }

        var nearestEdge = model.Edges
            .Where(edge => edge.Traversable || edge.IsRunway)
            .Where(edge => model.Nodes.ContainsKey(edge.FromNodeId) && model.Nodes.ContainsKey(edge.ToNodeId))
            .Select(edge =>
            {
                var from = model.Nodes[edge.FromNodeId];
                var to = model.Nodes[edge.ToNodeId];
                var distance = Geometry.DistancePointToSegment(x, z, from.X, from.Z, to.X, to.Z);
                return (Edge: edge, Distance: distance);
            })
            .OrderBy(item => item.Distance)
            .FirstOrDefault();

        if (nearestEdge.Edge is not null)
        {
            var limit = nearestEdge.Edge.IsRunway ? RunwaySnapMeters : TaxiwaySnapMeters;
            if (nearestEdge.Distance <= limit)
            {
                var from = model.Nodes[nearestEdge.Edge.FromNodeId];
                var to = model.Nodes[nearestEdge.Edge.ToNodeId];
                var nearestEndpoint = Geometry.Distance(x, z, from.X, from.Z)
                    <= Geometry.Distance(x, z, to.X, to.Z)
                        ? from.Id
                        : to.Id;
                return new GroundLocation(
                    nearestEdge.Edge.IsRunway ? GroundPositionKind.Runway : GroundPositionKind.Taxiway,
                    nearestEndpoint,
                    nearestEdge.Edge.SourceIndex,
                    nearestEdge.Distance,
                    Confidence(nearestEdge.Distance, limit),
                    nearestEdge.Edge.IsRunway
                        ? $"Piste {nearestEdge.Edge.RunwayNumber:00}"
                        : string.IsNullOrWhiteSpace(nearestEdge.Edge.TaxiwayName)
                            ? $"Segment taxi #{nearestEdge.Edge.SourceIndex}"
                            : $"Taxiway {nearestEdge.Edge.TaxiwayName}");
            }
        }

        return new GroundLocation(
            GroundPositionKind.Unknown,
            null,
            null,
            double.PositiveInfinity,
            0,
            "Position non raccordée de manière fiable au réseau de roulage.");
    }

    private static double Confidence(double distance, double limit) =>
        Math.Clamp(1.0 - (distance / Math.Max(1.0, limit)), 0.4, 1.0);
}

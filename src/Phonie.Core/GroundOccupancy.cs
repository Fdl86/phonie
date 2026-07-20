namespace Phonie.Core;

public static class GroundOccupancy
{
    private const double NodeOccupancyRadiusMeters = 30.0;
    private const double EdgeOccupancyRadiusMeters = 20.0;
    private static readonly TimeSpan MaximumTrafficAge = TimeSpan.FromSeconds(5);

    public static GroundOccupancySnapshot Build(
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
            return GroundOccupancySnapshot.Unknown(now, "Trafic SimConnect indisponible.");
        }

        var occupiedNodes = new HashSet<string>(StringComparer.Ordinal);
        var occupiedEdges = new HashSet<uint>();

        foreach (var contact in contacts)
        {
            if (!contact.IsOnGround
                || now - contact.Timestamp > MaximumTrafficAge
                || (userObjectId.HasValue && contact.ObjectId == userObjectId.Value))
            {
                continue;
            }

            var (x, z) = Geometry.ProjectLocal(model.Latitude, model.Longitude, contact.Latitude, contact.Longitude);

            foreach (var node in model.Nodes.Values)
            {
                if (Geometry.Distance(x, z, node.X, node.Z) <= NodeOccupancyRadiusMeters)
                {
                    occupiedNodes.Add(node.Id);
                }
            }

            foreach (var edge in model.Edges.Where(item => item.Traversable || item.IsRunway))
            {
                if (!model.Nodes.TryGetValue(edge.FromNodeId, out var from)
                    || !model.Nodes.TryGetValue(edge.ToNodeId, out var to))
                {
                    continue;
                }

                if (Geometry.DistancePointToSegment(x, z, from.X, from.Z, to.X, to.Z) <= EdgeOccupancyRadiusMeters)
                {
                    occupiedEdges.Add(edge.SourceIndex);
                }
            }
        }

        return new GroundOccupancySnapshot(
            now,
            OccupancyKnowledge.Available,
            occupiedNodes,
            occupiedEdges,
            "Objets avions SimConnect proches.");
    }
}

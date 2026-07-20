using System.Text.RegularExpressions;

namespace Phonie.Core;

public static partial class OperationalPointResolver
{
    public static IReadOnlyDictionary<string, OperationalPointResolution> Resolve(
        AirportGroundModel model,
        AirportOperationalProfile? profile)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = model.HoldShortPoints.ToDictionary(
            item => item.NodeId,
            item => BuildFallback(item),
            StringComparer.Ordinal);

        if (profile is null
            || !string.Equals(profile.Icao, model.Icao, StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }

        var claimedNodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var definition in profile.Points)
        {
            var resolution = MatchDefinition(model, definition, claimedNodes);
            if (resolution is null)
            {
                continue;
            }

            claimedNodes.Add(resolution.NodeId);
            if (definition.Role is OperationalPointRole.DepartureHoldingPoint or OperationalPointRole.IntermediateHoldingPoint)
            {
                result[resolution.NodeId] = resolution;
            }
        }

        return result;
    }

    public static OperationalPointResolution? ResolveRunwayEntry(
        AirportGroundModel model,
        AirportOperationalProfile? profile,
        OperationalPointResolution? departurePoint,
        RunwayEnd runway)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(runway);

        if (profile is null
            || departurePoint is null
            || string.IsNullOrWhiteSpace(departurePoint.RunwayEntryId))
        {
            return null;
        }

        var definition = profile.Points.FirstOrDefault(item =>
            string.Equals(item.Id, departurePoint.RunwayEntryId, StringComparison.OrdinalIgnoreCase)
            && item.Role == OperationalPointRole.RunwayEntry
            && AppliesToRunway(item, runway));
        return definition is null ? null : MatchDefinition(model, definition, new HashSet<string>(StringComparer.Ordinal));
    }

    public static string BuildDiagnostic(
        AirportGroundModel model,
        AirportOperationalProfile? profile,
        IReadOnlyDictionary<string, OperationalPointResolution> resolutions)
    {
        var lines = new List<string>
        {
            profile is null
                ? "Profil opérationnel : absent - résolution automatique Facilities."
                : $"Profil opérationnel : {profile.Icao} révision {profile.Revision} - {profile.Source}",
        };

        foreach (var hold in model.HoldShortPoints.OrderBy(item => item.NodeId, StringComparer.Ordinal))
        {
            if (!resolutions.TryGetValue(hold.NodeId, out var resolution))
            {
                continue;
            }

            lines.Add(
                $"- {hold.NodeId} : Facilities '{Display(hold.Label)}' -> radio '{Display(resolution.RadioLabel)}' " +
                $"- rôle {resolution.Role} - confiance {resolution.Confidence} - source {resolution.Source}");
        }

        if (profile is not null)
        {
            foreach (var definition in profile.Points.Where(item => item.Role == OperationalPointRole.RunwayEntry))
            {
                var matched = MatchDefinition(model, definition, new HashSet<string>(StringComparer.Ordinal));
                lines.Add(matched is null
                    ? $"- entrée {definition.Label} : non associée au graphe."
                    : $"- entrée {definition.Label} : associée à {matched.NodeId}, confiance {matched.Confidence}.");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static OperationalPointResolution BuildFallback(HoldShortPoint hold)
    {
        var clean = CleanLabel(hold.Label);
        var confidence = IsPlausibleRadioLabel(clean)
            ? OperationalLabelConfidence.Medium
            : OperationalLabelConfidence.Low;
        return new OperationalPointResolution(
            hold.NodeId,
            clean,
            confidence >= OperationalLabelConfidence.Medium ? clean : string.Empty,
            OperationalPointRole.Unknown,
            confidence,
            "Facilities API / scène",
            null,
            DepartureHandling.ControllerChoice,
            null);
    }

    private static OperationalPointResolution? MatchDefinition(
        AirportGroundModel model,
        OperationalPointDefinition definition,
        IReadOnlySet<string> excludedNodeIds)
    {
        var (targetX, targetZ) = Geometry.ProjectLocal(
            model.Latitude,
            model.Longitude,
            definition.Latitude,
            definition.Longitude);

        IEnumerable<GroundNode> candidates = definition.Role switch
        {
            OperationalPointRole.DepartureHoldingPoint or OperationalPointRole.IntermediateHoldingPoint =>
                model.HoldShortPoints
                    .Select(item => model.Nodes.GetValueOrDefault(item.NodeId))
                    .Where(item => item is not null)
                    .Cast<GroundNode>(),
            OperationalPointRole.RunwayEntry => model.Nodes.Values.Where(item => item.Kind != GroundNodeKind.Parking),
            _ => model.Nodes.Values,
        };

        var best = candidates
            .Where(item => !excludedNodeIds.Contains(item.Id))
            .Select(item => new
            {
                Node = item,
                Distance = Geometry.Distance(item.X, item.Z, targetX, targetZ),
            })
            .Where(item => item.Distance <= Math.Max(5, definition.MatchRadiusMeters))
            .OrderBy(item => item.Distance)
            .FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        var facilityLabel = model.HoldShortPoints
            .FirstOrDefault(item => string.Equals(item.NodeId, best.Node.Id, StringComparison.Ordinal))?.Label
            ?? best.Node.Label;
        return new OperationalPointResolution(
            best.Node.Id,
            CleanLabel(facilityLabel),
            CleanLabel(definition.Label),
            definition.Role,
            OperationalLabelConfidence.Official,
            $"Profil opérationnel {model.Icao}",
            definition.Id,
            definition.DepartureHandling,
            definition.RunwayEntryId);
    }

    public static bool AppliesToRunway(OperationalPointDefinition definition, RunwayEnd runway) =>
        definition.Runways.Count == 0
        || definition.Runways.Any(item =>
            string.Equals(item, runway.Designator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item, runway.Number.ToString("00"), StringComparison.OrdinalIgnoreCase));

    private static bool IsPlausibleRadioLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) && TaxiwayLabelRegex().IsMatch(value);

    private static string CleanLabel(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    [GeneratedRegex("^[A-Z]{1,2}[0-9]{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxiwayLabelRegex();
}

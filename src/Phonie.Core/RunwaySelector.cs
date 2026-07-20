namespace Phonie.Core;

public static class RunwaySelector
{
    public static RunwaySelection Select(AirportGroundModel model, double windFromDegrees, double windSpeedKnots)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (model.RunwayEnds.Count == 0)
        {
            return new RunwaySelection(false, null, "Aucune piste exploitable.", 0);
        }

        var longestRunwayLength = model.RunwayEnds.Max(item => item.RunwayLengthMeters);
        var candidates = model.RunwayEnds
            .Where(item => Math.Abs(item.RunwayLengthMeters - longestRunwayLength) < 1.0)
            .ToArray();

        if (!double.IsFinite(windFromDegrees) || !double.IsFinite(windSpeedKnots))
        {
            return new RunwaySelection(false, null, "Vent indisponible : piste en service indéterminée.", 0);
        }

        if (windSpeedKnots < 3.0)
        {
            var calm = candidates
                .OrderBy(item => item.RunwayIndex)
                .ThenBy(item => item.Number)
                .First();
            return new RunwaySelection(true, calm, "Vent faible : extrémité primaire déterministe.", 0.65);
        }

        var selected = candidates
            .OrderBy(item => Geometry.AngularDifference(item.HeadingDegrees, windFromDegrees))
            .ThenByDescending(item => item.RunwayLengthMeters)
            .First();
        var difference = Geometry.AngularDifference(selected.HeadingDegrees, windFromDegrees);
        var confidence = Math.Clamp(1.0 - (difference / 180.0), 0.5, 1.0);
        return new RunwaySelection(true, selected, $"Piste la plus favorable au vent, écart {difference:F0}°.", confidence);
    }
}

namespace Phonie.Core;

public static class Geometry
{
    private const double EarthRadiusMeters = 6_371_000.0;

    public static (double X, double Z) ProjectLocal(double originLatitude, double originLongitude, double latitude, double longitude)
    {
        var lat0 = DegreesToRadians(originLatitude);
        var lat = DegreesToRadians(latitude);
        var dLat = lat - lat0;
        var dLon = DegreesToRadians(longitude - originLongitude);
        var meanLat = (lat + lat0) / 2.0;

        var north = dLat * EarthRadiusMeters;
        var east = dLon * EarthRadiusMeters * Math.Cos(meanLat);
        return (east, north);
    }

    public static double Distance(double ax, double az, double bx, double bz) =>
        Math.Sqrt(((bx - ax) * (bx - ax)) + ((bz - az) * (bz - az)));

    public static double DistancePointToSegment(
        double px,
        double pz,
        double ax,
        double az,
        double bx,
        double bz)
    {
        var dx = bx - ax;
        var dz = bz - az;
        var lengthSquared = (dx * dx) + (dz * dz);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(px, pz, ax, az);
        }

        var t = (((px - ax) * dx) + ((pz - az) * dz)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(px, pz, ax + (t * dx), az + (t * dz));
    }

    public static double NormalizeHeading(double value)
    {
        if (!double.IsFinite(value))
        {
            return double.NaN;
        }

        var normalized = value % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    public static double AngularDifference(double left, double right)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right))
        {
            return double.PositiveInfinity;
        }

        var difference = Math.Abs(NormalizeHeading(left) - NormalizeHeading(right));
        return Math.Min(difference, 360.0 - difference);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

using System.Globalization;
using Phonie.Models;

namespace Phonie.Services;

public sealed class AtisService
{
    private static readonly string[] Letters =
    [
        "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel", "India", "Juliett",
        "Kilo", "Lima", "Mike", "November", "Oscar", "Papa", "Quebec", "Romeo", "Sierra", "Tango",
        "Uniform", "Victor", "Whiskey", "X-ray", "Yankee", "Zulu",
    ];

    private int letterIndex;
    private string? lastSignature;
    private AtisInformation? current;

    public AtisInformation? Current => this.current;

    public AtisInformation? Update(
        SimulatorSnapshot snapshot,
        AirportFacilityReport? report,
        string? operationalRunway = null)
    {
        if (report is null || report.Runways.Count == 0 || !HasUsableWeather(snapshot))
        {
            this.current = null;
            return null;
        }

        var selectedEnd = !string.IsNullOrWhiteSpace(operationalRunway) && operationalRunway != "-"
            ? operationalRunway
            : SelectFallbackRunway(report, snapshot);
        if (string.IsNullOrWhiteSpace(selectedEnd) || selectedEnd == "-")
        {
            this.current = null;
            return null;
        }

        var visibilityBucket = snapshot.VisibilityMeters >= 10_000
            ? 10_000
            : Math.Round(snapshot.VisibilityMeters / 500.0) * 500.0;
        var ceilingBucket = double.IsFinite(snapshot.CeilingFeet) && snapshot.CeilingFeet > 0
            ? Math.Round(snapshot.CeilingFeet / 100.0) * 100.0
            : -1;
        var dewPointBucket = double.IsFinite(snapshot.DewPointCelsius)
            ? Math.Round(snapshot.DewPointCelsius)
            : -999;

        var signature = string.Create(
            CultureInfo.InvariantCulture,
            $"{selectedEnd}|{Math.Round(snapshot.WindDirectionTrueDegrees / 10.0) * 10:000}|{Math.Round(snapshot.WindVelocityKnots / 2.0) * 2:F0}|{Math.Round(snapshot.QnhHpa):F0}|{Math.Round(snapshot.TemperatureCelsius):F0}|{dewPointBucket:F0}|{visibilityBucket:F0}|{ceilingBucket:F0}");

        if (this.lastSignature is not null && !string.Equals(this.lastSignature, signature, StringComparison.Ordinal))
        {
            this.letterIndex = (this.letterIndex + 1) % Letters.Length;
        }

        this.lastSignature = signature;
        var letter = Letters[this.letterIndex];
        var airportIcao = string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao;
        var airportName = string.IsNullOrWhiteSpace(report.Name) ? airportIcao : report.Name.Trim();

        var lines = new List<string>
        {
            $"{airportName}, information {letter}.",
            string.Empty,
            $"Vent {snapshot.WindDirectionTrueDegrees:000} degrés, {snapshot.WindVelocityKnots:F0} nœuds.",
            snapshot.VisibilityMeters >= 10_000
                ? "Visibilité supérieure à dix kilomètres."
                : $"Visibilité {Math.Max(0.1, snapshot.VisibilityMeters / 1000.0):F1} kilomètres.",
        };

        if (double.IsFinite(snapshot.CeilingFeet) && snapshot.CeilingFeet > 0)
        {
            lines.Add($"Plafond {Math.Round(snapshot.CeilingFeet / 100.0) * 100:F0} pieds.");
        }

        lines.Add($"Température {snapshot.TemperatureCelsius:F0} degrés.");
        if (double.IsFinite(snapshot.DewPointCelsius))
        {
            lines.Add($"Point de rosée {snapshot.DewPointCelsius:F0} degrés.");
        }

        lines.Add($"QNH {Math.Round(snapshot.QnhHpa):F0}.");
        lines.Add($"Piste en service {selectedEnd}.");
        lines.Add(string.Empty);
        lines.Add($"Accusez réception de l'information {letter}.");

        var text = string.Join(Environment.NewLine, lines);
        this.current = new AtisInformation(
            airportIcao,
            airportName,
            letter,
            selectedEnd,
            snapshot.WindDirectionTrueDegrees,
            snapshot.WindVelocityKnots,
            snapshot.QnhHpa,
            snapshot.TemperatureCelsius,
            snapshot.DewPointCelsius,
            snapshot.VisibilityMeters,
            snapshot.CeilingFeet,
            text,
            DateTimeOffset.Now,
            signature);
        return this.current;
    }

    private static bool HasUsableWeather(SimulatorSnapshot snapshot) =>
        double.IsFinite(snapshot.WindDirectionTrueDegrees)
        && double.IsFinite(snapshot.WindVelocityKnots)
        && double.IsFinite(snapshot.QnhHpa)
        && snapshot.QnhHpa is > 850 and < 1100
        && double.IsFinite(snapshot.TemperatureCelsius)
        && double.IsFinite(snapshot.VisibilityMeters)
        && snapshot.VisibilityMeters > 0;

    private static string SelectFallbackRunway(AirportFacilityReport report, SimulatorSnapshot snapshot)
    {
        var runway = report.Runways
            .Where(item => item.PrimaryNumber is >= 1 and <= 36
                && item.SecondaryNumber is >= 1 and <= 36
                && item.Surface != 255
                && item.LengthMeters > 300)
            .OrderByDescending(item => item.LengthMeters)
            .FirstOrDefault()
        ?? report.Runways
            .Where(item => item.PrimaryNumber is >= 1 and <= 36 && item.SecondaryNumber is >= 1 and <= 36)
            .OrderByDescending(item => item.LengthMeters)
            .FirstOrDefault();

        if (runway is null)
        {
            return "-";
        }

        if (snapshot.WindVelocityKnots < 3 || !double.IsFinite(snapshot.WindDirectionTrueDegrees))
        {
            return FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator);
        }

        var primaryHeading = Normalize(runway.HeadingDegrees);
        var secondaryHeading = Normalize(runway.HeadingDegrees + 180.0);
        return AngularDifference(primaryHeading, snapshot.WindDirectionTrueDegrees)
               <= AngularDifference(secondaryHeading, snapshot.WindDirectionTrueDegrees)
            ? FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator)
            : FormatRunwayEnd(runway.SecondaryNumber, runway.SecondaryDesignator);
    }

    private static double AngularDifference(double left, double right)
    {
        var difference = Math.Abs(Normalize(left) - Normalize(right));
        return Math.Min(difference, 360.0 - difference);
    }

    private static double Normalize(double value) => (value % 360.0 + 360.0) % 360.0;

    public static string FormatRunwayEnd(int number, int designator)
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

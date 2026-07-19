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

    public AtisInformation? Update(SimulatorSnapshot snapshot, AirportFacilityReport? report)
    {
        if (report is null || report.Runways.Count == 0 || !HasUsableWeather(snapshot))
        {
            return this.current;
        }

        var runway = SelectMainRunway(report);
        if (runway is null)
        {
            return this.current;
        }

        var selectedEnd = SelectRunwayEnd(runway, snapshot.WindDirectionTrueDegrees, snapshot.WindVelocityKnots);
        var visibilityBucket = snapshot.VisibilityMeters >= 10_000 ? 10_000 : Math.Round(snapshot.VisibilityMeters / 500.0) * 500.0;
        var signature = string.Create(CultureInfo.InvariantCulture,
            $"{selectedEnd}|{Math.Round(snapshot.WindDirectionTrueDegrees / 10.0) * 10:000}|{Math.Round(snapshot.WindVelocityKnots / 2.0) * 2:F0}|{Math.Round(snapshot.QnhHpa):F0}|{Math.Round(snapshot.TemperatureCelsius):F0}|{visibilityBucket:F0}");

        if (this.lastSignature is not null && !string.Equals(this.lastSignature, signature, StringComparison.Ordinal))
        {
            this.letterIndex = (this.letterIndex + 1) % Letters.Length;
        }

        this.lastSignature = signature;
        var letter = Letters[this.letterIndex];
        var airportName = string.IsNullOrWhiteSpace(report.Name) ? report.Icao : report.Name;
        var visibilityText = snapshot.VisibilityMeters >= 10_000
            ? "Visibilité supérieure à dix kilomètres."
            : $"Visibilité {Math.Max(0.1, snapshot.VisibilityMeters / 1000.0):F1} kilomètres.";

        var text =
            $"Poitiers-Biard, information {letter}.\n\n" +
            $"Vent {snapshot.WindDirectionTrueDegrees:000} degrés, {snapshot.WindVelocityKnots:F0} noeuds.\n" +
            visibilityText + "\n" +
            $"Température {snapshot.TemperatureCelsius:F0} degrés.\n" +
            $"QNH {Math.Round(snapshot.QnhHpa):F0}.\n" +
            $"Piste proposée {selectedEnd}.\n\n" +
            $"Accusez réception de l'information {letter}.";

        this.current = new AtisInformation(
            string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao,
            airportName,
            letter,
            selectedEnd,
            snapshot.WindDirectionTrueDegrees,
            snapshot.WindVelocityKnots,
            snapshot.QnhHpa,
            snapshot.TemperatureCelsius,
            snapshot.VisibilityMeters,
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

    private static AirportRunwayData? SelectMainRunway(AirportFacilityReport report) =>
        report.Runways
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

    private static string SelectRunwayEnd(AirportRunwayData runway, double windFromDegrees, double windKnots)
    {
        if (windKnots < 3 || !double.IsFinite(windFromDegrees))
        {
            return FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator);
        }

        var primaryHeading = Normalize(runway.HeadingDegrees);
        var secondaryHeading = Normalize(runway.HeadingDegrees + 180.0);
        var primaryDifference = AngularDifference(primaryHeading, windFromDegrees);
        var secondaryDifference = AngularDifference(secondaryHeading, windFromDegrees);
        return primaryDifference <= secondaryDifference
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

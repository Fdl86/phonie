using System.Text;
using System.Text.Json;
using Phonie.Core;
using Phonie.Models;

namespace Phonie.Services;

public sealed class GroundOperationsCoordinator
{
    private const double OwnAircraftFallbackRadiusMeters = 20.0;
    private readonly object sync = new();
    private readonly GroundOperationsEngine engine = new();
    private AirportGroundModel? airport;
    private AircraftGroundObservation? aircraft;
    private GroundTrafficSnapshot traffic = new(
        DateTimeOffset.MinValue,
        false,
        Array.Empty<GroundTrafficContactData>(),
        "Trafic SimConnect en attente.");
    private GroundOccupancySnapshot occupancy = GroundOccupancySnapshot.Unknown(
        DateTimeOffset.UtcNow,
        "Trafic SimConnect en attente.");
    private IReadOnlyList<GroundTrafficOccupancyDiagnostic> occupancyDiagnostics =
        Array.Empty<GroundTrafficOccupancyDiagnostic>();
    private GroundLocation? location;
    private RunwaySelection runway = new(false, null, "Piste en attente.", 0);
    private ControllerDecision? lastDecision;
    private double ownLatitude;
    private double ownLongitude;
    private bool hasOwnPosition;
    private string ownCallsign = string.Empty;

    public event EventHandler<string>? LogMessage;

    public AirportGroundModel? Airport
    {
        get
        {
            lock (this.sync)
            {
                return this.airport;
            }
        }
    }

    public string? CurrentRunwayDesignator
    {
        get
        {
            lock (this.sync)
            {
                return this.runway.RunwayEnd?.Designator;
            }
        }
    }

    public void UpdateAirport(AirportFacilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string message;
        lock (this.sync)
        {
            var snapshot = new FacilityAirportSnapshot(
                string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao,
                report.Name,
                report.Latitude,
                report.Longitude,
                report.Runways.Select(item => new FacilityRunway(
                    item.Index,
                    item.Latitude,
                    item.Longitude,
                    item.HeadingDegrees,
                    item.LengthMeters,
                    item.WidthMeters,
                    item.Surface,
                    item.PrimaryNumber,
                    item.PrimaryDesignator,
                    item.SecondaryNumber,
                    item.SecondaryDesignator)).ToArray(),
                report.TaxiPoints.Select(item => new FacilityTaxiPoint(
                    item.Index,
                    item.Type,
                    item.Orientation,
                    item.BiasX,
                    item.BiasZ)).ToArray(),
                report.TaxiParkings.Select(item => new FacilityParking(
                    item.Index,
                    item.Type,
                    item.TaxiPointType,
                    item.Name,
                    item.Suffix,
                    item.Number,
                    item.Orientation,
                    item.HeadingDegrees,
                    item.RadiusMeters,
                    item.BiasX,
                    item.BiasZ)).ToArray(),
                report.TaxiPaths.Select(item => new FacilityTaxiPath(
                    item.Index,
                    item.Type,
                    item.WidthMeters,
                    item.WeightLimit,
                    item.RunwayNumber,
                    item.RunwayDesignator,
                    item.StartIndex,
                    item.EndIndex,
                    item.NameIndex)).ToArray(),
                report.TaxiNames.Select(item => new FacilityTaxiName(item.Index, item.Name)).ToArray());

            this.airport = AirportGroundModelBuilder.Build(snapshot);
            this.RecomputeDerivedStateLocked();
            this.UpdateTrafficLocked(this.traffic, this.ownCallsign);
            message =
                $"Moteur sol {this.airport.Icao} : {this.airport.Nodes.Count} nœud(s), " +
                $"{this.airport.Edges.Count} segment(s), {this.airport.HoldShortPoints.Count} point(s) d'attente, " +
                $"{this.airport.Warnings.Count} avertissement(s).";
        }

        this.PublishLog(message);
    }

    public void UpdateSnapshot(SimulatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (this.sync)
        {
            this.UpdateSnapshotLocked(snapshot);
        }
    }

    public void UpdateTraffic(GroundTrafficSnapshot snapshot, string ownCallsign)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (this.sync)
        {
            this.UpdateTrafficLocked(snapshot, ownCallsign);
        }
    }

    public ControllerDecision Process(
        string pilotText,
        SimulatorSnapshot? snapshot,
        OperationalFrequency frequency)
    {
        ArgumentNullException.ThrowIfNull(frequency);

        ControllerDecision decision;
        lock (this.sync)
        {
            var radio = new RadioContext(
                frequency.Kind switch
                {
                    OperationalRadioKind.Controlled => ServiceCapability.Controlled,
                    OperationalRadioKind.InformationService => ServiceCapability.InformationOnly,
                    OperationalRadioKind.AutomaticBroadcast => ServiceCapability.AutomaticBroadcast,
                    OperationalRadioKind.RecordedMessage => ServiceCapability.RecordedMessage,
                    OperationalRadioKind.SelfInformation => ServiceCapability.SelfInformation,
                    _ => ServiceCapability.Unknown,
                },
                frequency.ServiceName,
                frequency.DialogueAllowed,
                frequency.Source);

            if (snapshot is not null)
            {
                this.UpdateSnapshotLocked(snapshot);
            }

            decision = this.engine.Process(
                pilotText,
                snapshot?.AircraftAtcId,
                radio,
                this.airport,
                this.aircraft,
                this.occupancy,
                snapshot?.WindDirectionTrueDegrees ?? double.NaN,
                snapshot?.WindVelocityKnots ?? double.NaN);
            this.lastDecision = decision;
        }

        this.SaveDecision(decision);
        return decision;
    }

    public GroundOperationsUiState GetUiState()
    {
        lock (this.sync)
        {
            var route = this.engine.Session.AssignedTaxiRoute;
            var routeText = route is { Success: true }
                ? route.TaxiwayNames.Count > 0
                    ? string.Join(" - ", route.TaxiwayNames)
                    : $"{route.TotalDistanceMeters:F0} m sans nom de voie"
                : "-";

            var occupancyText = this.occupancy.Knowledge == OccupancyKnowledge.Available
                ? $"{this.occupancy.OccupiedNodeIds.Count} nœud(s), {this.occupancy.OccupiedEdgeIds.Count} segment(s) occupé(s)"
                : "INCONNUE - aucune clairance de roulage";

            return new GroundOperationsUiState(
                this.airport?.Icao ?? "-",
                this.airport is null
                    ? "Graphe en attente"
                    : this.airport.IsUsable
                        ? $"{this.airport.Nodes.Count} nœuds / {this.airport.Edges.Count} segments / {this.airport.HoldShortPoints.Count} attentes"
                        : "Graphe non exploitable",
                this.location?.Description ?? "Position en attente",
                this.engine.Session.State.ToString(),
                this.runway.RunwayEnd?.Designator ?? "-",
                this.engine.Session.AssignedHoldShort?.Label ?? "-",
                routeText,
                occupancyText,
                this.lastDecision?.Confidence ?? this.location?.Confidence ?? 0,
                this.lastDecision is null
                    ? "Aucune décision"
                    : $"{this.lastDecision.ReasonCode} - {this.lastDecision.SystemMessage}".TrimEnd(' ', '-'),
                this.BuildGroundDiagnosticLocked());
        }
    }

    private void UpdateSnapshotLocked(SimulatorSnapshot snapshot)
    {
        this.aircraft = new AircraftGroundObservation(
            snapshot.Timestamp,
            snapshot.Latitude,
            snapshot.Longitude,
            snapshot.GroundSpeedKnots,
            snapshot.IsOnGround,
            snapshot.HeadingMagneticDegrees);
        this.ownLatitude = snapshot.Latitude;
        this.ownLongitude = snapshot.Longitude;
        this.hasOwnPosition = double.IsFinite(snapshot.Latitude) && double.IsFinite(snapshot.Longitude);
        this.ownCallsign = CallsignFormatter.Normalize(snapshot.AircraftAtcId);
        this.RecomputeDerivedStateLocked(snapshot.WindDirectionTrueDegrees, snapshot.WindVelocityKnots);
        this.UpdateTrafficLocked(this.traffic, this.ownCallsign);
        this.engine.Observe(this.airport, this.aircraft);
    }

    private void UpdateTrafficLocked(GroundTrafficSnapshot snapshot, string ownCallsign)
    {
        this.traffic = snapshot;
        if (this.airport is null)
        {
            this.occupancy = GroundOccupancySnapshot.Unknown(snapshot.Timestamp, "Graphe aérodrome en attente.");
            this.occupancyDiagnostics = Array.Empty<GroundTrafficOccupancyDiagnostic>();
            return;
        }

        var normalizedOwn = CallsignFormatter.Normalize(ownCallsign);
        if (string.IsNullOrWhiteSpace(normalizedOwn))
        {
            normalizedOwn = this.ownCallsign;
        }

        var contacts = snapshot.Contacts
            .Where(item => !this.IsOwnAircraft(item, normalizedOwn))
            .Select(item => new GroundTrafficContact(
                item.ObjectId,
                item.Callsign,
                item.Latitude,
                item.Longitude,
                item.GroundSpeedKnots,
                item.IsOnGround,
                item.Timestamp))
            .ToArray();

        var result = GroundOccupancy.BuildWithDiagnostics(
            this.airport,
            contacts,
            snapshot.Timestamp,
            snapshot.ProviderAvailable,
            userObjectId: 0);
        this.occupancy = result.Snapshot;
        this.occupancyDiagnostics = result.Contacts;
    }

    private bool IsOwnAircraft(GroundTrafficContactData contact, string normalizedOwn)
    {
        // Valeur officielle SimConnect pour l'objet utilisateur.
        if (contact.ObjectId == 0)
        {
            return true;
        }

        var normalizedContact = CallsignFormatter.Normalize(contact.Callsign);
        if (!string.IsNullOrWhiteSpace(normalizedOwn)
            && string.Equals(normalizedContact, normalizedOwn, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!this.hasOwnPosition)
        {
            return false;
        }

        var (east, north) = Geometry.ProjectLocal(
            this.ownLatitude,
            this.ownLongitude,
            contact.Latitude,
            contact.Longitude);
        var distance = Geometry.Distance(0, 0, east, north);
        if (distance > OwnAircraftFallbackRadiusMeters)
        {
            return false;
        }

        // Secours pour les paquets dont l'ATC ID est vide ou transitoirement
        // différent. La vitesse proche évite d'exclure arbitrairement un trafic
        // réellement voisin sur l'aire de stationnement.
        var ownSpeed = this.aircraft?.GroundSpeedKnots ?? double.NaN;
        return !double.IsFinite(ownSpeed)
            || Math.Abs(ownSpeed - contact.GroundSpeedKnots) <= 2.0;
    }

    private void RecomputeDerivedStateLocked(double windDirection = double.NaN, double windSpeed = double.NaN)
    {
        if (this.airport is null)
        {
            return;
        }

        if (this.aircraft is not null)
        {
            this.location = GroundLocator.Locate(this.airport, this.aircraft);
        }

        if (double.IsFinite(windDirection) && double.IsFinite(windSpeed))
        {
            this.runway = RunwaySelector.Select(this.airport, windDirection, windSpeed);
        }
    }

    private string BuildGroundDiagnosticLocked()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Aérodrome : {this.airport?.Icao ?? "-"}");
        builder.AppendLine($"Avion : {this.location?.Description ?? "position inconnue"} - nœud {this.location?.NodeId ?? "-"} - segment {this.location?.EdgeId?.ToString() ?? "-"}");
        builder.AppendLine($"Piste : {this.runway.RunwayEnd?.Designator ?? "-"} - {this.runway.Reason}");
        builder.AppendLine($"Trafic brut : {this.traffic.Status}");
        builder.AppendLine($"Occupation retenue : {this.occupancy.OccupiedNodeIds.Count} nœud(s), {this.occupancy.OccupiedEdgeIds.Count} segment(s) - {this.occupancy.Source}");

        if (this.occupancyDiagnostics.Count == 0)
        {
            builder.AppendLine("Objets trafic : aucun contact analysé.");
        }
        else
        {
            builder.AppendLine("Objets trafic analysés :");
            foreach (var item in this.occupancyDiagnostics.OrderBy(item => item.ObjectId))
            {
                var callsign = string.IsNullOrWhiteSpace(item.Callsign) ? "sans indicatif" : item.Callsign;
                var parking = item.NearestParkingNodeId is null || !item.NearestParkingDistanceMeters.HasValue
                    ? "parking -"
                    : $"parking {item.NearestParkingNodeId} à {item.NearestParkingDistanceMeters.Value:F1} m";
                var edge = item.NearestEdgeId.HasValue && item.NearestEdgeDistanceMeters.HasValue
                    ? $"segment {item.NearestEdgeId.Value} à {item.NearestEdgeDistanceMeters.Value:F1} m"
                    : "segment -";
                var blockedNodes = item.OccupiedNodeIds.Count == 0
                    ? "aucun nœud"
                    : $"nœuds {string.Join(",", item.OccupiedNodeIds)}";
                var blockedEdges = item.OccupiedEdgeIds.Count == 0
                    ? "aucun segment"
                    : $"segments {string.Join(",", item.OccupiedEdgeIds)}";
                builder.AppendLine(
                    $"- objet {item.ObjectId} [{callsign}] {item.GroundSpeedKnots:F1} kt : {item.Classification} - {parking} - {edge} - bloque {blockedNodes}, {blockedEdges}. {item.Reason}");
            }
        }

        if (this.airport is not null
            && this.location is not null
            && this.runway.RunwayEnd is not null)
        {
            builder.AppendLine("Itinéraires candidats :");
            builder.AppendLine(TaxiRouter.BuildDiagnostic(this.airport, this.location, this.runway.RunwayEnd, this.occupancy));
        }
        else
        {
            builder.AppendLine("Itinéraires candidats : diagnostic en attente du graphe, de la position ou de la piste.");
        }

        if (this.lastDecision is not null)
        {
            builder.AppendLine($"Dernière décision : {this.lastDecision.ReasonCode} - {this.lastDecision.SystemMessage}");
        }

        return builder.ToString().TrimEnd();
    }

    private void SaveDecision(ControllerDecision decision)
    {
        try
        {
            GroundLocation? location;
            GroundOccupancySnapshot occupancy;
            IReadOnlyList<GroundTrafficOccupancyDiagnostic> trafficDiagnostics;
            string routingDiagnostic;
            lock (this.sync)
            {
                location = this.location;
                occupancy = this.occupancy;
                trafficDiagnostics = this.occupancyDiagnostics.ToArray();
                routingDiagnostic = this.BuildGroundDiagnosticLocked();
            }

            Directory.CreateDirectory(AppPaths.GroundOperationsDirectory);
            var path = Path.Combine(
                AppPaths.GroundOperationsDirectory,
                $"ground-decisions-{DateTime.Now:yyyyMMdd}.jsonl");
            var line = JsonSerializer.Serialize(new
            {
                Timestamp = DateTimeOffset.Now,
                decision.Action,
                decision.ReasonCode,
                decision.SpokenText,
                decision.SystemMessage,
                decision.StateBefore,
                decision.StateAfter,
                decision.FullCallsign,
                decision.ShortCallsign,
                decision.Confidence,
                Runway = decision.TaxiRoute?.Runway?.Designator,
                HoldShort = decision.TaxiRoute?.HoldShort?.Label,
                Taxiways = decision.TaxiRoute?.TaxiwayNames,
                DistanceMeters = decision.TaxiRoute?.TotalDistanceMeters,
                PositionKind = location?.Kind.ToString(),
                PositionNode = location?.NodeId,
                PositionEdge = location?.EdgeId,
                PositionDescription = location?.Description,
                OccupancyKnowledge = occupancy.Knowledge.ToString(),
                OccupiedNodeCount = occupancy.OccupiedNodeIds.Count,
                OccupiedEdgeCount = occupancy.OccupiedEdgeIds.Count,
                OccupiedNodes = occupancy.OccupiedNodeIds.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                OccupiedEdges = occupancy.OccupiedEdgeIds.OrderBy(item => item).ToArray(),
                OccupancySource = occupancy.Source,
                TrafficDiagnostics = trafficDiagnostics,
                RoutingDiagnostic = routingDiagnostic,
            });
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            this.PublishLog($"Journal moteur sol impossible : {CleanMessage(exception)}");
        }
    }

    private void PublishLog(string message) => this.LogMessage?.Invoke(this, message);

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }
}

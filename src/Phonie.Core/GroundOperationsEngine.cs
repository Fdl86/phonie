namespace Phonie.Core;

public sealed class GroundOperationsEngine
{
    private static readonly TimeSpan AcknowledgementReminderInterval = TimeSpan.FromSeconds(9);
    private readonly GroundSession session = new();

    public GroundSession Session => this.session;

    public ControllerDecision Process(
        string pilotText,
        string? simulatorCallsign,
        RadioContext radio,
        AirportGroundModel? airport,
        AircraftGroundObservation? aircraft,
        GroundOccupancySnapshot occupancy,
        double windDirectionDegrees,
        double windSpeedKnots,
        AirportOperationalProfile? profile = null,
        double qnhHpa = double.NaN)
    {
        var stateBefore = this.session.State;
        var fullCallsign = CallsignFormatter.Normalize(simulatorCallsign);
        if (!string.IsNullOrWhiteSpace(fullCallsign))
        {
            this.session.FullCallsign = fullCallsign;
            this.session.AuthorizedShortCallsign = CallsignFormatter.BuildShort(fullCallsign);
        }

        var spokenFull = CallsignFormatter.SpeakFull(this.session.FullCallsign);
        var spokenShort = CallsignFormatter.SpeakShort(this.session.FullCallsign);
        var intentDetails = PilotIntentParser.ParseDetailed(pilotText);
        var intent = intentDetails.Intent;
        this.session.LastPilotRequest = pilotText.Trim();
        var requiresAcknowledgement = radio.Capability is ServiceCapability.Controlled or ServiceCapability.InformationOnly;

        if (!radio.DialogueAllowed)
        {
            return Decision(
                ControllerAction.Silent,
                "RADIO_SILENT",
                string.Empty,
                radio.Capability switch
                {
                    ServiceCapability.AutomaticBroadcast => "ATIS/AWS : diffusion automatique, aucune réponse au PTT.",
                    ServiceCapability.RecordedMessage => "Message enregistré : aucune réponse au PTT.",
                    ServiceCapability.SelfInformation => "Auto-information : PHONIE reste silencieux.",
                    _ => "Station non identifiée : PHONIE reste silencieux.",
                },
                stateBefore,
                stateBefore,
                null,
                1,
                false);
        }

        if (string.IsNullOrWhiteSpace(this.session.FullCallsign))
        {
            return Decision(
                ControllerAction.RequestClarification,
                "CALLSIGN_MISSING",
                "Station appelante, répétez votre indicatif.",
                "Indicatif SimConnect indisponible ou non fiable.",
                stateBefore,
                stateBefore,
                null,
                0.4,
                requiresAcknowledgement);
        }

        if (airport is null || !airport.IsUsable)
        {
            return Message(
                ControllerAction.Unable,
                "AIRPORT_DATA_UNAVAILABLE",
                $"{CurrentCallsign(spokenFull, spokenShort)}, impossible de déterminer le réseau de roulage.",
                "Données Facilities absentes ou invalides.",
                null,
                0.2);
        }

        if (aircraft is null)
        {
            return Message(
                ControllerAction.Unable,
                "AIRCRAFT_POSITION_UNAVAILABLE",
                $"{CurrentCallsign(spokenFull, spokenShort)}, position non déterminée, maintenez.",
                "Observation avion absente.",
                null,
                0.2);
        }

        var location = GroundLocator.Locate(airport, aircraft);
        this.UpdateObservedState(location, aircraft, profile, airport);

        if (intent == PilotIntent.Unknown)
        {
            return Message(
                ControllerAction.RequestClarification,
                "INTENT_UNKNOWN",
                $"{CurrentCallsign(spokenFull, spokenShort)}, précisez vos intentions.",
                "Intention essentielle non reconnue.",
                null,
                0.45);
        }

        if (intent == PilotIntent.Readback)
        {
            return Decision(
                ControllerAction.Silent,
                "READBACK_RECEIVED",
                string.Empty,
                "Collationnement reçu au PTT. Le contenu est journalisé mais non bloquant dans cette version.",
                stateBefore,
                this.session.State,
                this.session.AssignedTaxiRoute,
                1,
                false);
        }

        if (intent == PilotIntent.RepeatRequest)
        {
            if (string.IsNullOrWhiteSpace(this.session.LastControllerInstruction))
            {
                return Message(
                    ControllerAction.RequestClarification,
                    "NOTHING_TO_REPEAT",
                    $"{CurrentCallsign(spokenFull, spokenShort)}, aucune instruction précédente, précisez votre demande.",
                    "Aucune instruction mémorisée.",
                    null,
                    0.7);
            }

            return Message(
                ControllerAction.Speak,
                "REPEAT_LAST",
                this.session.LastControllerInstruction,
                "Répétition de la dernière instruction sans recalcul.",
                this.session.AssignedTaxiRoute,
                1);
        }

        if (intent == PilotIntent.StartupRequest)
        {
            if (location.Kind != GroundPositionKind.Parking)
            {
                return Incompatible("STARTUP_NOT_AT_PARKING", "demande de mise en route incompatible avec la position actuelle.");
            }

            this.session.State = GroundSessionState.StartupRequested;
            var startupText = radio.Capability == ServiceCapability.InformationOnly
                ? $"{CurrentCallsign(spokenFull, spokenShort)}, mise en route à votre convenance, rappelez prêt au roulage."
                : $"{CurrentCallsign(spokenFull, spokenShort)}, mise en route approuvée, rappelez prêt au roulage.";
            this.EstablishContact(startupText);
            return Message(
                ControllerAction.Speak,
                radio.Capability == ServiceCapability.InformationOnly ? "AFIS_STARTUP_INFORMATION" : "STARTUP_APPROVED",
                startupText,
                radio.Capability == ServiceCapability.InformationOnly
                    ? "AFIS : information uniquement, aucune autorisation de contrôle."
                    : string.Empty,
                null,
                0.95);
        }

        if (intent == PilotIntent.InitialContact)
        {
            var text = location.Kind switch
            {
                GroundPositionKind.Parking => radio.Capability == ServiceCapability.InformationOnly
                    ? $"{spokenFull}, {radio.StationName}, bonjour, transmettez vos intentions."
                    : $"{spokenFull}, {radio.StationName}, bonjour, rappelez prêt au roulage.",
                GroundPositionKind.HoldShort => $"{spokenFull}, {radio.StationName}, bonjour, rappelez prêt au départ.",
                _ => $"{spokenFull}, {radio.StationName}, bonjour, précisez vos intentions.",
            };
            this.EstablishContact(text);
            return Message(ControllerAction.Speak, "INITIAL_CONTACT", text, string.Empty, null, 0.9);
        }

        if (intent == PilotIntent.TaxiRequest)
        {
            if (this.session.State is GroundSessionState.TaxiClearanceIssued or GroundSessionState.Taxiing
                && this.session.AssignedTaxiRoute is { Success: true } assignedRoute
                && this.session.AssignedRunway is not null)
            {
                var reminder = radio.Capability == ServiceCapability.InformationOnly
                    ? $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)} en service, rappelez prêt au point d'attente."
                    : $"{CurrentCallsign(spokenFull, spokenShort)}, poursuivez vers le point d'attente et rappelez prêt.";
                return Message(
                    ControllerAction.Speak,
                    "TAXI_CLEARANCE_ALREADY_ISSUED",
                    reminder,
                    "Itinéraire déjà attribué : aucun recalcul ni changement de point d'attente.",
                    assignedRoute,
                    assignedRoute.Confidence);
            }

            if (location.Kind is not (GroundPositionKind.Parking or GroundPositionKind.Taxiway))
            {
                return Incompatible("TAXI_POSITION_INCOMPATIBLE", "roulage demandé depuis une position incompatible.");
            }

            var runwaySelection = RunwaySelector.Select(airport, windDirectionDegrees, windSpeedKnots, profile);
            if (!runwaySelection.Success || runwaySelection.RunwayEnd is null)
            {
                return Unable("RUNWAY_UNKNOWN", "impossible de déterminer la piste en service.");
            }

            if (occupancy.Knowledge == OccupancyKnowledge.Unknown)
            {
                return Unable("OCCUPANCY_UNKNOWN", "trafic au sol non déterminé, maintenez position.");
            }

            var route = TaxiRouter.RouteToNearestAvailableHoldShort(
                airport,
                location,
                runwaySelection.RunwayEnd,
                occupancy,
                profile);
            if (!route.Success || route.HoldShort is null)
            {
                return Unable("ROUTE_UNAVAILABLE", route.FailureReason);
            }

            this.session.AssignedRunway = runwaySelection.RunwayEnd;
            this.session.AssignedHoldShort = route.HoldShort;
            this.session.AssignedTaxiRoute = route;
            this.session.AssignedOperationalPoint = route.OperationalPoint;
            this.session.AssignedRunwayEntry = route.RunwayEntry;
            this.session.State = GroundSessionState.TaxiClearanceIssued;

            string text;
            string reason;
            string system;
            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                text =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(runwaySelection.RunwayEnd.Designator)} en service, " +
                    $"{BuildWindAndQnh(windDirectionDegrees, windSpeedKnots, qnhHpa)}, rappelez prêt au point d'attente.";
                reason = "AFIS_TAXI_INFORMATION";
                system = "AFIS : piste et paramètres transmis. L'itinéraire est calculé uniquement pour le diagnostic, sans clairance impérative de roulage.";
            }
            else
            {
                text = $"{CurrentCallsign(spokenFull, spokenShort)}, roulez au point d'attente et rappelez prêt.";
                reason = "TAXI_CLEARANCE_GENERIC_HOLD";
                var internalPoint = route.OperationalPoint?.RadioLabel ?? route.HoldShort.Label;
                system =
                    $"Itinéraire interne calculé vers {internalPoint} ({route.HoldShort.NodeId}) pour la piste {runwaySelection.RunwayEnd.Designator}. " +
                    "L'appellation locale n'est volontairement pas prononcée.";
            }

            this.EstablishContact(text);
            return Message(ControllerAction.Speak, reason, text, system, route, route.Confidence);
        }

        if (intent is PilotIntent.ReadyAtHoldShort or PilotIntent.ReadyForIntersectionDeparture)
        {
            if (!IsAtDepartureHoldShort(airport, location, profile))
            {
                return Incompatible(
                    "READY_NOT_AT_DEPARTURE_HOLD",
                    BuildReadyPositionMismatch(location));
            }

            // Les noms de points et la demande d'intersection ne pilotent plus la décision.
            // La position géométrique au point d'attente et l'état du trafic sont les seules conditions opérationnelles.
            this.session.State = GroundSessionState.ReadyForDeparture;

            if (this.session.AssignedRunway is null)
            {
                return Unable("RUNWAY_NOT_ASSIGNED", "piste attribuée non disponible.");
            }

            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                var runwayOccupied = IsRunwayOccupied(airport, this.session.AssignedRunway, occupancy);
                var trafficText = runwayOccupied
                    ? "trafic signalé sur la piste"
                    : "aucun trafic signalé sur la piste";
                var text =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)} en service, " +
                    $"{BuildWindAndQnh(windDirectionDegrees, windSpeedKnots, qnhHpa)}, {trafficText}.";
                return Message(
                    ControllerAction.Speak,
                    "AFIS_READY_INFORMATION",
                    text,
                    "AFIS : renseignements uniquement, aucune autorisation d'alignement ou de décollage.",
                    this.session.AssignedTaxiRoute,
                    0.9);
            }

            if (occupancy.Knowledge == OccupancyKnowledge.Unknown)
            {
                var waitText = $"{CurrentCallsign(spokenFull, spokenShort)}, maintenez point d'attente, trafic non déterminé.";
                return Message(
                    ControllerAction.Speak,
                    "TRAFFIC_STATUS_UNKNOWN_HOLD",
                    waitText,
                    "État du trafic indisponible : aucune autorisation d'entrée ni de décollage.",
                    this.session.AssignedTaxiRoute,
                    0.75);
            }

            if (IsRunwayOccupied(airport, this.session.AssignedRunway, occupancy))
            {
                var waitText = $"{CurrentCallsign(spokenFull, spokenShort)}, maintenez point d'attente, trafic sur la piste.";
                return Message(
                    ControllerAction.Speak,
                    "RUNWAY_OCCUPIED_HOLD",
                    waitText,
                    "Piste occupée : aucune autorisation d'entrée ni de décollage.",
                    this.session.AssignedTaxiRoute,
                    0.95);
            }

            this.session.State = GroundSessionState.TakeoffCleared;
            var takeoffText =
                $"{CurrentCallsign(spokenFull, spokenShort)}, alignez-vous piste {SpeakRunway(this.session.AssignedRunway.Designator)}, " +
                $"vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}, autorisé décollage.";
            return Message(
                ControllerAction.Speak,
                "LINEUP_TAKEOFF_CLEARED_FROM_HOLD",
                takeoffText,
                "Avion confirmé au point d'attente de départ et piste libre selon le trafic disponible. Les appellations locales ne participent pas à la décision.",
                this.session.AssignedTaxiRoute,
                0.96);
        }

        if (intent == PilotIntent.BacktrackRequest)
        {
            if (!IsAtDepartureHoldShort(airport, location, profile)
                || this.session.AssignedRunway is null)
            {
                return Incompatible("BACKTRACK_INCOMPATIBLE", "remontée de piste impossible depuis la position actuelle.");
            }

            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                var text =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)} en service, " +
                    "remontée de piste à votre appréciation, aucun trafic signalé.";
                return Message(
                    ControllerAction.Speak,
                    "AFIS_BACKTRACK_INFORMATION",
                    text,
                    "AFIS : information uniquement.",
                    this.session.AssignedTaxiRoute,
                    0.8);
            }

            this.session.State = GroundSessionState.BacktrackCleared;
            var backtrack =
                $"{CurrentCallsign(spokenFull, spokenShort)}, remontez piste {SpeakRunway(this.session.AssignedRunway.Designator)}, " +
                "alignez-vous et attendez.";
            return Message(ControllerAction.Speak, "BACKTRACK_CLEARED", backtrack, string.Empty, this.session.AssignedTaxiRoute, 0.95);
        }

        if (intent == PilotIntent.LineUpRequest)
        {
            if (!IsAtDepartureHoldShort(airport, location, profile)
                || this.session.AssignedRunway is null)
            {
                return Incompatible("LINEUP_INCOMPATIBLE", "alignement impossible depuis la position ou l'état actuel.");
            }

            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                var info =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)} en service, " +
                    "aucun trafic signalé sur la piste.";
                return Message(ControllerAction.Speak, "AFIS_LINEUP_INFORMATION", info, "AFIS : information uniquement.", this.session.AssignedTaxiRoute, 0.85);
            }

            this.session.State = GroundSessionState.LineUpCleared;
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, alignez-vous piste {SpeakRunway(this.session.AssignedRunway.Designator)} et attendez.";
            return Message(ControllerAction.Speak, "LINEUP_CLEARED", text, string.Empty, this.session.AssignedTaxiRoute, 0.95);
        }

        if (intent == PilotIntent.TakeoffRequest)
        {
            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                var info =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway?.Designator ?? "-")} en service, " +
                    $"vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}.";
                return Message(ControllerAction.Speak, "AFIS_TAKEOFF_INFORMATION", info, "AFIS : aucune autorisation de décollage.", this.session.AssignedTaxiRoute, 0.8);
            }

            if (location.Kind != GroundPositionKind.Runway
                || this.session.State is not (GroundSessionState.LineUpCleared or GroundSessionState.LinedUp or GroundSessionState.Backtracking))
            {
                return Incompatible("TAKEOFF_INCOMPATIBLE", "décollage refusé : avion non aligné sur la piste attribuée.");
            }

            this.session.State = GroundSessionState.TakeoffCleared;
            var runway = this.session.AssignedRunway?.Designator ?? "-";
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(runway)}, autorisé décollage, " +
                $"vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}.";
            return Message(ControllerAction.Speak, "TAKEOFF_CLEARED", text, string.Empty, this.session.AssignedTaxiRoute, 0.95);
        }

        if (intent == PilotIntent.LineUpAndTakeoffRequest)
        {
            if (!IsAtDepartureHoldShort(airport, location, profile)
                || this.session.AssignedRunway is null)
            {
                return Incompatible("LINEUP_TAKEOFF_INCOMPATIBLE", "demande alignement et décollage incompatible avec la situation.");
            }

            if (radio.Capability == ServiceCapability.InformationOnly)
            {
                var info =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)} en service, " +
                    $"vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}.";
                return Message(ControllerAction.Speak, "AFIS_LINEUP_TAKEOFF_INFORMATION", info, "AFIS : aucune clairance de contrôle.", this.session.AssignedTaxiRoute, 0.85);
            }

            if (occupancy.Knowledge == OccupancyKnowledge.Unknown)
            {
                var waitText = $"{CurrentCallsign(spokenFull, spokenShort)}, maintenez point d'attente, trafic non déterminé.";
                return Message(
                    ControllerAction.Speak,
                    "TRAFFIC_STATUS_UNKNOWN_HOLD",
                    waitText,
                    "État du trafic indisponible : aucune autorisation d'entrée ni de décollage.",
                    this.session.AssignedTaxiRoute,
                    0.75);
            }

            if (IsRunwayOccupied(airport, this.session.AssignedRunway, occupancy))
            {
                var waitText = $"{CurrentCallsign(spokenFull, spokenShort)}, maintenez point d'attente, trafic sur la piste.";
                return Message(
                    ControllerAction.Speak,
                    "RUNWAY_OCCUPIED_HOLD",
                    waitText,
                    "Piste occupée : aucune autorisation d'entrée ni de décollage.",
                    this.session.AssignedTaxiRoute,
                    0.95);
            }

            this.session.State = GroundSessionState.TakeoffCleared;
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, alignez-vous piste {SpeakRunway(this.session.AssignedRunway.Designator)}, " +
                $"vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}, autorisé décollage.";
            return Message(
                ControllerAction.Speak,
                "LINEUP_TAKEOFF_CLEARED_FROM_HOLD",
                text,
                "Demande combinée acceptée depuis un point d'attente de départ, piste libre selon le trafic disponible.",
                this.session.AssignedTaxiRoute,
                0.96);
        }

        return Message(
            ControllerAction.RequestClarification,
            "UNHANDLED_INTENT",
            $"{CurrentCallsign(spokenFull, spokenShort)}, précisez votre demande.",
            "Intention reconnue mais non traitée dans cet état.",
            null,
            0.4);

        ControllerDecision Message(
            ControllerAction action,
            string reason,
            string text,
            string system,
            TaxiRoute? route,
            double confidence)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                this.session.LastControllerInstruction = text;
            }

            return Decision(
                action,
                reason,
                text,
                system,
                stateBefore,
                this.session.State,
                route,
                confidence,
                requiresAcknowledgement && !string.IsNullOrWhiteSpace(text));
        }

        ControllerDecision Unable(string reason, string detail)
        {
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, impossible, {detail}";
            return Message(ControllerAction.Unable, reason, text, detail, null, 0.3);
        }

        ControllerDecision Incompatible(string reason, string detail)
        {
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, impossible, {detail}";
            return Message(ControllerAction.Unable, reason, text, detail, null, 0.5);
        }
    }

    public void Observe(
        AirportGroundModel? airport,
        AircraftGroundObservation? observation,
        AirportOperationalProfile? profile = null)
    {
        if (airport is null || observation is null)
        {
            return;
        }

        var location = GroundLocator.Locate(airport, observation);
        this.UpdateObservedState(location, observation, profile, airport);
    }

    public void ArmPilotAcknowledgement(DateTimeOffset now, TimeSpan? timeout = null)
    {
        this.session.AwaitingPilotAcknowledgement = true;
        this.session.AcknowledgementDeadline = now + (timeout ?? AcknowledgementReminderInterval);
        this.session.AcknowledgementReminderCount = 0;
    }

    public bool AcknowledgePilotPtt()
    {
        if (!this.session.AwaitingPilotAcknowledgement)
        {
            return false;
        }

        this.session.AwaitingPilotAcknowledgement = false;
        this.session.AcknowledgementDeadline = null;
        this.session.AcknowledgementReminderCount = 0;
        return true;
    }

    public ControllerDecision? PollAcknowledgement(DateTimeOffset now)
    {
        if (!this.session.AwaitingPilotAcknowledgement
            || !this.session.AcknowledgementDeadline.HasValue
            || now < this.session.AcknowledgementDeadline.Value)
        {
            return null;
        }

        this.session.AcknowledgementReminderCount++;
        this.session.AcknowledgementDeadline = now + AcknowledgementReminderInterval;
        var callsign = CallsignFormatter.SpeakShort(this.session.FullCallsign);
        if (string.IsNullOrWhiteSpace(callsign))
        {
            callsign = CallsignFormatter.SpeakFull(this.session.FullCallsign);
        }

        var text = this.session.AcknowledgementReminderCount == 1
            ? $"{callsign}, collationnez."
            : $"{callsign}, accusez réception.";
        this.session.LastControllerInstruction = text;
        return Decision(
            ControllerAction.Speak,
            "ACKNOWLEDGEMENT_REMINDER",
            text,
            $"Aucun PTT reçu après le message précédent - relance {this.session.AcknowledgementReminderCount}.",
            this.session.State,
            this.session.State,
            this.session.AssignedTaxiRoute,
            1,
            false);
    }

    private void UpdateObservedState(
        GroundLocation location,
        AircraftGroundObservation observation,
        AirportOperationalProfile? profile,
        AirportGroundModel airport)
    {
        if (!observation.IsOnGround || location.Kind == GroundPositionKind.Airborne)
        {
            this.session.State = GroundSessionState.Airborne;
            return;
        }

        switch (location.Kind)
        {
            case GroundPositionKind.Parking:
                if (this.session.State is GroundSessionState.Unknown or GroundSessionState.Airborne)
                {
                    this.session.State = GroundSessionState.Parked;
                }
                break;
            case GroundPositionKind.Taxiway:
                if (observation.GroundSpeedKnots > 2
                    && this.session.State is (GroundSessionState.TaxiClearanceIssued or GroundSessionState.ReadyToTaxi))
                {
                    this.session.State = GroundSessionState.Taxiing;
                }
                break;
            case GroundPositionKind.HoldShort:
                var resolutions = OperationalPointResolver.Resolve(airport, profile);
                var resolution = location.NodeId is null ? null : resolutions.GetValueOrDefault(location.NodeId);
                if (resolution?.Role == OperationalPointRole.IntermediateHoldingPoint)
                {
                    this.session.State = GroundSessionState.AtIntermediateHoldingPoint;
                }
                else if (this.session.AssignedHoldShort is null
                    || string.Equals(location.NodeId, this.session.AssignedHoldShort.NodeId, StringComparison.Ordinal))
                {
                    this.session.State = GroundSessionState.AtHoldShort;
                }
                break;
            case GroundPositionKind.Runway:
                if (this.session.State == GroundSessionState.BacktrackCleared)
                {
                    this.session.State = GroundSessionState.Backtracking;
                }
                else if (this.session.State is GroundSessionState.LineUpCleared or GroundSessionState.EnteringRunway)
                {
                    this.session.State = GroundSessionState.LinedUp;
                }
                break;
        }
    }

    private bool IsAtDepartureHoldShort(
        AirportGroundModel airport,
        GroundLocation location,
        AirportOperationalProfile? profile)
    {
        if (location.Kind != GroundPositionKind.HoldShort
            || string.IsNullOrWhiteSpace(location.NodeId)
            || this.session.AssignedRunway is null)
        {
            return false;
        }

        var holdShort = airport.HoldShortPoints.FirstOrDefault(item =>
            string.Equals(item.NodeId, location.NodeId, StringComparison.Ordinal));
        if (holdShort is null)
        {
            return false;
        }

        var resolutions = OperationalPointResolver.Resolve(airport, profile);
        var resolution = resolutions.GetValueOrDefault(location.NodeId);
        if (resolution?.Role == OperationalPointRole.IntermediateHoldingPoint)
        {
            return false;
        }

        var isAssignedNode = this.session.AssignedHoldShort is not null
            && string.Equals(location.NodeId, this.session.AssignedHoldShort.NodeId, StringComparison.Ordinal);
        var isAssociatedRunway = holdShort.AssociatedRunwayIndex == this.session.AssignedRunway.RunwayIndex
            || holdShort.NearestRunwayNumber == this.session.AssignedRunway.Number;
        return isAssignedNode || isAssociatedRunway;
    }

    private static string BuildReadyPositionMismatch(GroundLocation location) =>
        $"position actuelle {location.Description} non confirmée à un point d'attente de départ lié à la piste attribuée ; maintenez et rappelez prêt au point d'attente.";

    private static bool IsRunwayOccupied(
        AirportGroundModel airport,
        RunwayEnd runway,
        GroundOccupancySnapshot occupancy)
    {
        var runwayEdgeIds = airport.Edges
            .Where(item => item.IsRunway
                && (item.RunwayNumber == runway.Number || item.RunwayNumber is null))
            .Select(item => item.SourceIndex)
            .ToHashSet();
        return occupancy.OccupiedEdgeIds.Any(runwayEdgeIds.Contains);
    }

    private void EstablishContact(string instruction)
    {
        this.session.ContactEstablished = true;
        this.session.LastControllerInstruction = instruction;
        if (this.session.State == GroundSessionState.Unknown)
        {
            this.session.State = GroundSessionState.Parked;
        }
    }

    private string CurrentCallsign(string full, string shortForm) =>
        this.session.ContactEstablished && !string.IsNullOrWhiteSpace(shortForm) ? shortForm : full;

    private ControllerDecision Decision(
        ControllerAction action,
        string reason,
        string spoken,
        string system,
        GroundSessionState before,
        GroundSessionState after,
        TaxiRoute? route,
        double confidence,
        bool requiresAcknowledgement) =>
        new(
            action,
            reason,
            spoken,
            system,
            before,
            after,
            route,
            this.session.FullCallsign,
            this.session.AuthorizedShortCallsign,
            confidence,
            requiresAcknowledgement);

    private static string BuildWindAndQnh(double direction, double speed, double qnh)
    {
        var parts = new List<string>();
        if (double.IsFinite(direction) && double.IsFinite(speed))
        {
            parts.Add($"vent {SpeakWind(direction, speed)}");
        }

        if (double.IsFinite(qnh) && qnh is > 850 and < 1100)
        {
            parts.Add($"QNH {SpeakInteger((int)Math.Round(qnh))}");
        }

        return parts.Count == 0 ? "paramètres indisponibles" : string.Join(", ", parts);
    }

    private static string SpeakWind(double direction, double speed)
    {
        if (!double.IsFinite(direction) || !double.IsFinite(speed))
        {
            return "indisponible";
        }

        var roundedDirection = ((int)Math.Round(direction / 10.0) * 10) % 360;
        if (roundedDirection == 0)
        {
            roundedDirection = 360;
        }

        var roundedSpeed = Math.Max(0, (int)Math.Round(speed));
        return $"{SpeakInteger(roundedDirection)} degrés, {SpeakInteger(roundedSpeed)} nœuds";
    }

    private static string JoinFrench(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return SpeakTaxiway(names[0]);
        }

        return string.Join(", ", names.Take(names.Count - 1).Select(SpeakTaxiway))
            + " et "
            + SpeakTaxiway(names[^1]);
    }

    private static string SpeakTaxiway(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "non nommé";
        }

        var trimmed = value.Trim().ToUpperInvariant();
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            return CallsignFormatter.SpeakLetter(trimmed[0]);
        }

        if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1..].All(char.IsDigit))
        {
            var letter = CallsignFormatter.SpeakLetter(trimmed[0]);
            return $"{letter} {string.Join(" ", trimmed[1..].Select(SpeakDigit))}";
        }

        return trimmed;
    }

    private static string SpeakRunway(string designator)
    {
        if (string.IsNullOrWhiteSpace(designator) || designator == "-")
        {
            return "non déterminée";
        }

        var digits = designator.TakeWhile(char.IsDigit).Select(SpeakDigit);
        var suffix = new string(designator.SkipWhile(char.IsDigit).ToArray()) switch
        {
            "L" => "gauche",
            "R" => "droite",
            "C" => "centrale",
            _ => string.Empty,
        };
        return string.Join(" ", digits) + (string.IsNullOrWhiteSpace(suffix) ? string.Empty : $" {suffix}");
    }

    private static string SpeakInteger(int value)
    {
        if (value == 0)
        {
            return "zéro";
        }

        return string.Join(" ", Math.Abs(value).ToString().Select(SpeakDigit));
    }

    private static string SpeakDigit(char digit) => digit switch
    {
        '0' => "zéro",
        '1' => "un",
        '2' => "deux",
        '3' => "trois",
        '4' => "quatre",
        '5' => "cinq",
        '6' => "six",
        '7' => "sept",
        '8' => "huit",
        '9' => "neuf",
        _ => digit.ToString(),
    };
}

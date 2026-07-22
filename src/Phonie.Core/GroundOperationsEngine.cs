namespace Phonie.Core;

public sealed class GroundOperationsEngine
{
    private static readonly TimeSpan AcknowledgementReminderInterval = TimeSpan.FromSeconds(9);
    private readonly GroundSession session = new();
    private readonly Dictionary<string, RadioContactState> contacts = new(StringComparer.OrdinalIgnoreCase);
    private RadioContactState? currentContact;
    private string aircraftIdentity = string.Empty;

    public GroundSession Session => this.session;

    public IReadOnlyCollection<RadioContactState> ContactHistory => this.contacts.Values.ToArray();

    public void Reset() => this.ResetFlightSession();

    public void ResetFlightSession()
    {
        this.contacts.Clear();
        this.currentContact = null;
        this.aircraftIdentity = string.Empty;
        this.ResetGroundContext(clearAircraftIdentity: true);
    }

    public void ResetGroundContext() => this.ResetGroundContext(clearAircraftIdentity: false);

    private void ResetGroundContext(bool clearAircraftIdentity)
    {
        this.session.FullCallsign = clearAircraftIdentity ? string.Empty : this.aircraftIdentity;
        this.session.AuthorizedShortCallsign = string.Empty;
        this.session.ContactEstablished = false;
        this.session.State = GroundSessionState.Unknown;
        this.session.LastPilotRequest = string.Empty;
        this.session.LastControllerInstruction = string.Empty;
        this.session.AssignedRunway = null;
        this.session.AssignedHoldShort = null;
        this.session.AssignedTaxiRoute = null;
        this.session.AssignedOperationalPoint = null;
        this.session.AssignedRunwayEntry = null;
        this.session.AwaitingPilotAcknowledgement = false;
        this.session.AcknowledgementDeadline = null;
        this.session.AcknowledgementReminderCount = 0;
    }

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
            if (!string.IsNullOrWhiteSpace(this.aircraftIdentity)
                && !string.Equals(this.aircraftIdentity, fullCallsign, StringComparison.OrdinalIgnoreCase))
            {
                this.ResetFlightSession();
            }

            this.aircraftIdentity = fullCallsign;
            this.session.FullCallsign = fullCallsign;
        }

        this.currentContact = this.GetOrCreateContact(radio);
        this.SyncSessionContact();
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

        if (ClearlyCallsAnotherService(pilotText, radio))
        {
            return Decision(
                ControllerAction.Silent,
                "CALLED_STATION_MISMATCH",
                string.Empty,
                "Une autre station ou un autre service est clairement appelé : PHONIE ne répond pas.",
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

        if (IsRegionalInformation(radio)
            && intent is PilotIntent.StartupRequest or PilotIntent.TaxiRequest or PilotIntent.ReadyAtHoldShort
                or PilotIntent.ReadyForIntersectionDeparture or PilotIntent.LineUpRequest
                or PilotIntent.LineUpAndTakeoffRequest or PilotIntent.TakeoffRequest or PilotIntent.BacktrackRequest)
        {
            return Message(
                ControllerAction.Unable,
                "REGIONAL_FIS_GROUND_REQUEST",
                $"{CurrentCallsign(spokenFull, spokenShort)}, impossible, service d'information de vol régional.",
                "Un SIV/FIS régional ne délivre aucune instruction de roulage, d'alignement ou de décollage.",
                null,
                1);
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
            var alreadyKnown = this.currentContact?.ContactEstablished == true;
            var explicitReturn = ContainsReturnGreeting(pilotText);
            var greeting = explicitReturn ? "rebonjour" : DetectGreeting(pilotText);
            var callsign = CurrentCallsign(spokenFull, spokenShort);
            var contactPhrase = !alreadyKnown
                ? $"{radio.StationName}, {greeting}"
                : explicitReturn
                    ? "rebonjour"
                    : string.Empty;
            var address = string.IsNullOrWhiteSpace(contactPhrase) ? callsign : $"{callsign}, {contactPhrase}";
            var text = location.Kind switch
            {
                GroundPositionKind.Parking => radio.Capability == ServiceCapability.InformationOnly
                    ? $"{address}, transmettez vos intentions."
                    : $"{address}, rappelez prêt au roulage.",
                GroundPositionKind.HoldShort => $"{address}, rappelez prêt au départ.",
                _ => $"{address}, précisez vos intentions.",
            };
            this.EstablishContact(text, greeting, countGreeting: !alreadyKnown || explicitReturn);
            return Message(ControllerAction.Speak, explicitReturn ? "RETURN_CONTACT" : "INITIAL_CONTACT", text, string.Empty, null, 0.9);
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
                $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)}, " +
                $"alignez-vous et autorisé décollage, vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}.";
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
                $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)}, alignez-vous et attendez.";
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
                $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(this.session.AssignedRunway.Designator)}, " +
                $"alignez-vous et autorisé décollage, vent {SpeakWind(windDirectionDegrees, windSpeedKnots)}.";
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
                text = this.DecorateFirstResponse(text, pilotText, radio, spokenFull);
                this.session.LastControllerInstruction = text;
                this.EstablishContact(text, DetectGreeting(pilotText), countGreeting: this.currentContact?.GreetingCount == 0);
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
                // Any genuine Facilities HOLD_SHORT is valid. Profile roles are diagnostic only.
                this.session.State = GroundSessionState.AtHoldShort;
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
        _ = profile; // Profiles never authorize or reject a departure point.
        if (location.Kind != GroundPositionKind.HoldShort
            || string.IsNullOrWhiteSpace(location.NodeId))
        {
            return false;
        }

        // The geometry is authoritative: every node built as HOLD_SHORT is acceptable.
        return airport.HoldShortPoints.Any(item =>
            string.Equals(item.NodeId, location.NodeId, StringComparison.Ordinal));
    }

    private static string BuildReadyPositionMismatch(GroundLocation location) =>
        $"position actuelle {location.Description} non confirmée à un vrai point d'attente Facilities ; maintenez et rappelez prêt au point d'attente.";

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

    private void EstablishContact(string instruction, string greeting = "", bool countGreeting = false)
    {
        this.session.ContactEstablished = true;
        this.session.LastControllerInstruction = instruction;
        if (this.currentContact is not null)
        {
            var now = DateTimeOffset.UtcNow;
            this.currentContact.ContactEstablished = true;
            this.currentContact.FullCallsignExchanged = true;
            this.currentContact.AuthorizedShortCallsign = CallsignFormatter.BuildShort(this.session.FullCallsign);
            this.currentContact.FirstContactAt ??= now;
            this.currentContact.LastContactAt = now;
            if (countGreeting)
            {
                this.currentContact.GreetingCount++;
                this.currentContact.LastGreeting = greeting;
            }
        }

        this.SyncSessionContact();
        if (this.session.State == GroundSessionState.Unknown)
        {
            this.session.State = GroundSessionState.Parked;
        }
    }

    private string CurrentCallsign(string full, string shortForm) =>
        this.currentContact?.ContactEstablished == true
            && this.currentContact.FullCallsignExchanged
            && !string.IsNullOrWhiteSpace(shortForm)
                ? shortForm
                : full;

    private RadioContactState GetOrCreateContact(RadioContext radio)
    {
        var key = BuildStationKey(radio);
        if (!this.contacts.TryGetValue(key, out var contact))
        {
            contact = new RadioContactState
            {
                StationKey = key,
                StationName = radio.StationName,
            };
            this.contacts[key] = contact;
        }
        else if (!string.IsNullOrWhiteSpace(radio.StationName))
        {
            contact.StationName = radio.StationName;
        }

        return contact;
    }

    private void SyncSessionContact()
    {
        this.session.ContactEstablished = this.currentContact?.ContactEstablished == true;
        this.session.AuthorizedShortCallsign = this.currentContact?.AuthorizedShortCallsign ?? string.Empty;
    }

    private string DecorateFirstResponse(string text, string pilotText, RadioContext radio, string spokenFull)
    {
        if (this.currentContact?.ContactEstablished == true || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var greeting = DetectGreeting(pilotText);
        var prefix = $"{spokenFull}, {radio.StationName}, {greeting}";
        var comma = text.IndexOf(',');
        return comma >= 0 ? prefix + text[comma..] : prefix + ", " + text;
    }

    private static string BuildStationKey(RadioContext radio)
    {
        if (!string.IsNullOrWhiteSpace(radio.StationKey))
        {
            return radio.StationKey.Trim().ToUpperInvariant();
        }

        return string.Join("|",
            string.IsNullOrWhiteSpace(radio.AirportIcao) ? "REGIONAL" : radio.AirportIcao.Trim().ToUpperInvariant(),
            radio.StationName.Trim().ToUpperInvariant(),
            radio.ServiceRole.Trim().ToUpperInvariant());
    }

    private static string DetectGreeting(string text)
    {
        var normalized = NormalizeRadioText(text);
        if (normalized.Contains("bonsoir", StringComparison.Ordinal)) return "bonsoir";
        if (normalized.Contains("rebonjour", StringComparison.Ordinal)
            || normalized.Contains("re bonjour", StringComparison.Ordinal)
            || normalized.Contains("de retour", StringComparison.Ordinal)) return "rebonjour";
        return "bonjour";
    }

    private static bool ContainsReturnGreeting(string text)
    {
        var normalized = NormalizeRadioText(text);
        return normalized.Contains("rebonjour", StringComparison.Ordinal)
            || normalized.Contains("re bonjour", StringComparison.Ordinal)
            || normalized.Contains("de retour", StringComparison.Ordinal);
    }

    private static bool IsRegionalInformation(RadioContext radio) =>
        radio.Capability == ServiceCapability.InformationOnly
        && (radio.Scope.Equals("Regional", StringComparison.OrdinalIgnoreCase)
            || radio.ServiceRole.Contains("SIV", StringComparison.OrdinalIgnoreCase)
            || radio.ServiceRole.Contains("FIS", StringComparison.OrdinalIgnoreCase)
            || radio.ServiceRole.Contains("FLIGHT", StringComparison.OrdinalIgnoreCase));

    private static bool ClearlyCallsAnotherService(string text, RadioContext radio)
    {
        var normalized = NormalizeRadioText(text);
        var tokenArray = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokens = tokenArray.ToHashSet(StringComparer.Ordinal);
        var roleToken = tokens.Contains("tour") ? "tour"
            : tokens.Contains("sol") ? "sol"
            : tokens.Contains("approche") ? "approche"
            : tokens.Contains("depart") ? "depart"
            : tokens.Contains("information") ? "information"
            : tokens.Contains("info") ? "info"
            : tokens.Contains("afis") ? "afis"
            : tokens.Contains("siv") ? "siv"
            : string.Empty;
        var calledRole = roleToken switch
        {
            "tour" => "TOWER",
            "sol" => "GROUND",
            "approche" => "APPROACH",
            "depart" => "DEPARTURE",
            "information" or "info" or "afis" or "siv" => "INFORMATION",
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(calledRole))
        {
            return false;
        }

        var roleIndex = Array.IndexOf(tokenArray, roleToken);
        if (roleIndex is > 0 and <= 3)
        {
            var calledPlace = tokenArray[roleIndex - 1];
            var currentStation = NormalizeRadioText(radio.StationName);
            if (calledPlace.Length >= 4
                && !currentStation.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(calledPlace, StringComparer.Ordinal))
            {
                return true;
            }
        }

        var role = radio.ServiceRole.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(role))
        {
            role = radio.StationName.Trim().ToUpperInvariant();
        }

        return calledRole switch
        {
            "TOWER" => !role.Contains("TWR", StringComparison.Ordinal) && !role.Contains("TOUR", StringComparison.Ordinal) && !role.Contains("TOWER", StringComparison.Ordinal),
            "GROUND" => !role.Contains("GND", StringComparison.Ordinal) && !role.Contains("SOL", StringComparison.Ordinal) && !role.Contains("GROUND", StringComparison.Ordinal),
            "APPROACH" => !role.Contains("APP", StringComparison.Ordinal) && !role.Contains("APPROCHE", StringComparison.Ordinal),
            "DEPARTURE" => !role.Contains("DEP", StringComparison.Ordinal) && !role.Contains("DEPART", StringComparison.Ordinal),
            "INFORMATION" => !role.Contains("INFO", StringComparison.Ordinal) && !role.Contains("AFIS", StringComparison.Ordinal) && !role.Contains("SIV", StringComparison.Ordinal) && !role.Contains("FIS", StringComparison.Ordinal),
            _ => false,
        };
    }

    private static string NormalizeRadioText(string value)
    {
        var normalized = (value ?? string.Empty).ToLowerInvariant()
            .Replace('é', 'e').Replace('è', 'e').Replace('ê', 'e').Replace('à', 'a').Replace('ù', 'u').Replace('ô', 'o').Replace('î', 'i');
        return string.Join(' ', normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

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

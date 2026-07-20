namespace Phonie.Core;

public sealed class GroundOperationsEngine
{
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
        double windSpeedKnots)
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
        var intent = PilotIntentParser.Parse(pilotText);
        this.session.LastPilotRequest = pilotText.Trim();

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
                1);
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
                0.4);
        }

        if (radio.Capability == ServiceCapability.InformationOnly)
        {
            var informationText = $"{CurrentCallsign(spokenFull, spokenShort)}, {radio.StationName}, bonjour. Transmettez votre position, altitude et destination.";
            this.EstablishContact(informationText);
            return Decision(
                ControllerAction.Speak,
                "INFORMATION_SERVICE",
                informationText,
                "Service d'information : aucune clairance de contrôle émise.",
                stateBefore,
                this.session.State,
                null,
                0.9);
        }

        if (radio.Capability != ServiceCapability.Controlled)
        {
            return Decision(
                ControllerAction.Silent,
                "SERVICE_NOT_CONTROLLED",
                string.Empty,
                "Le type de service ne permet pas une clairance.",
                stateBefore,
                stateBefore,
                null,
                1);
        }

        if (airport is null || !airport.IsUsable)
        {
            return Decision(
                ControllerAction.Unable,
                "AIRPORT_DATA_UNAVAILABLE",
                $"{CurrentCallsign(spokenFull, spokenShort)}, impossible de déterminer le réseau de roulage.",
                "Données Facilities absentes ou invalides.",
                stateBefore,
                stateBefore,
                null,
                0.2);
        }

        if (aircraft is null)
        {
            return Decision(
                ControllerAction.Unable,
                "AIRCRAFT_POSITION_UNAVAILABLE",
                $"{CurrentCallsign(spokenFull, spokenShort)}, position non déterminée, maintenez.",
                "Observation avion absente.",
                stateBefore,
                stateBefore,
                null,
                0.2);
        }

        var location = GroundLocator.Locate(airport, aircraft);
        this.UpdateObservedState(location, aircraft);

        if (intent == PilotIntent.Unknown)
        {
            return Decision(
                ControllerAction.RequestClarification,
                "INTENT_UNKNOWN",
                $"{CurrentCallsign(spokenFull, spokenShort)}, précisez vos intentions.",
                "Intention essentielle non reconnue.",
                stateBefore,
                this.session.State,
                null,
                0.45);
        }

        if (intent == PilotIntent.RepeatRequest)
        {
            if (string.IsNullOrWhiteSpace(this.session.LastControllerInstruction))
            {
                return Decision(
                    ControllerAction.RequestClarification,
                    "NOTHING_TO_REPEAT",
                    $"{CurrentCallsign(spokenFull, spokenShort)}, aucune clairance précédente, précisez votre demande.",
                    "Aucune instruction mémorisée.",
                    stateBefore,
                    this.session.State,
                    null,
                    0.7);
            }

            return Decision(
                ControllerAction.Speak,
                "REPEAT_LAST",
                this.session.LastControllerInstruction,
                "Répétition de la dernière instruction sans recalcul.",
                stateBefore,
                this.session.State,
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
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, mise en route approuvée, rappelez prêt au roulage.";
            this.EstablishContact(text);
            return Speak("STARTUP_APPROVED", text, null, 0.95);
        }

        if (intent is PilotIntent.InitialContact)
        {
            var text = location.Kind switch
            {
                GroundPositionKind.Parking => $"{spokenFull}, {radio.StationName}, bonjour, rappelez prêt au roulage.",
                GroundPositionKind.HoldShort => $"{spokenFull}, {radio.StationName}, bonjour, rappelez prêt au départ.",
                _ => $"{spokenFull}, {radio.StationName}, bonjour, précisez vos intentions.",
            };
            this.EstablishContact(text);
            return Speak("INITIAL_CONTACT", text, null, 0.9);
        }

        if (intent == PilotIntent.TaxiRequest)
        {
            if (this.session.State is GroundSessionState.TaxiClearanceIssued or GroundSessionState.Taxiing
                && this.session.AssignedTaxiRoute is { Success: true } assignedRoute
                && this.session.AssignedHoldShort is not null
                && this.session.AssignedRunway is not null)
            {
                var reminder =
                    $"{CurrentCallsign(spokenFull, spokenShort)}, poursuivez vers le point d'attente " +
                    $"{SpeakTaxiway(this.session.AssignedHoldShort.Label)} piste " +
                    $"{SpeakRunway(this.session.AssignedRunway.Designator)}.";
                return Speak(
                    "TAXI_CLEARANCE_ALREADY_ISSUED",
                    reminder,
                    assignedRoute,
                    assignedRoute.Confidence,
                    "Clairance déjà attribuée : aucun recalcul ni changement de point d'attente.");
            }

            if (location.Kind is not (GroundPositionKind.Parking or GroundPositionKind.Taxiway))
            {
                return Incompatible("TAXI_POSITION_INCOMPATIBLE", "roulage demandé depuis une position incompatible.");
            }

            var runwaySelection = RunwaySelector.Select(airport, windDirectionDegrees, windSpeedKnots);
            if (!runwaySelection.Success || runwaySelection.RunwayEnd is null)
            {
                return Unable("RUNWAY_UNKNOWN", "impossible de déterminer la piste en service.");
            }

            if (occupancy.Knowledge == OccupancyKnowledge.Unknown)
            {
                return Unable("OCCUPANCY_UNKNOWN", "trafic au sol non déterminé, maintenez position.");
            }

            var route = TaxiRouter.RouteToNearestAvailableHoldShort(airport, location, runwaySelection.RunwayEnd, occupancy);
            if (!route.Success || route.HoldShort is null)
            {
                return Unable("ROUTE_UNAVAILABLE", route.FailureReason);
            }

            this.session.AssignedRunway = runwaySelection.RunwayEnd;
            this.session.AssignedHoldShort = route.HoldShort;
            this.session.AssignedTaxiRoute = route;
            this.session.State = GroundSessionState.TaxiClearanceIssued;

            var via = route.TaxiwayNames.Count > 0
                ? $" via {JoinFrench(route.TaxiwayNames)}"
                : string.Empty;
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, roulez au point d'attente {SpeakTaxiway(route.HoldShort.Label)} " +
                $"piste {SpeakRunway(route.Runway!.Designator)}{via}, rappelez prêt.";
            this.EstablishContact(text);
            return Speak("TAXI_CLEARANCE", text, route, route.Confidence);
        }

        if (intent == PilotIntent.ReadyAtHoldShort)
        {
            if (location.Kind != GroundPositionKind.HoldShort)
            {
                return Incompatible("READY_NOT_AT_HOLD", "avion non localisé au point d'attente.");
            }

            this.session.State = GroundSessionState.ReadyForDeparture;
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, maintenez point d'attente, rappelez prêt pour alignement.";
            return Speak("READY_ACKNOWLEDGED", text, this.session.AssignedTaxiRoute, 0.9);
        }

        if (intent == PilotIntent.LineUpRequest)
        {
            if (location.Kind != GroundPositionKind.HoldShort || this.session.AssignedRunway is null)
            {
                return Incompatible("LINEUP_INCOMPATIBLE", "alignement impossible depuis la position ou l'état actuel.");
            }

            this.session.State = GroundSessionState.LineUpCleared;
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, alignez-vous piste {SpeakRunway(this.session.AssignedRunway.Designator)} et attendez.";
            return Speak("LINEUP_CLEARED", text, this.session.AssignedTaxiRoute, 0.95);
        }

        if (intent == PilotIntent.TakeoffRequest)
        {
            if (location.Kind != GroundPositionKind.Runway
                || this.session.State is not (GroundSessionState.LineUpCleared or GroundSessionState.LinedUp))
            {
                return Incompatible("TAKEOFF_INCOMPATIBLE", "décollage refusé : avion non aligné sur la piste attribuée.");
            }

            this.session.State = GroundSessionState.TakeoffCleared;
            var runway = this.session.AssignedRunway?.Designator ?? "-";
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, piste {SpeakRunway(runway)}, autorisé décollage.";
            return Speak("TAKEOFF_CLEARED", text, this.session.AssignedTaxiRoute, 0.95);
        }

        if (intent == PilotIntent.LineUpAndTakeoffRequest)
        {
            if (location.Kind != GroundPositionKind.HoldShort || this.session.AssignedRunway is null)
            {
                return Incompatible("LINEUP_TAKEOFF_INCOMPATIBLE", "demande alignement et décollage incompatible avec la situation.");
            }

            this.session.State = GroundSessionState.LineUpCleared;
            var text =
                $"{CurrentCallsign(spokenFull, spokenShort)}, alignez-vous piste {SpeakRunway(this.session.AssignedRunway.Designator)} et attendez.";
            return Speak(
                "LINEUP_ONLY_PENDING_RUNWAY_CHECK",
                text,
                this.session.AssignedTaxiRoute,
                0.9,
                "La demande combinée est reconnue, mais le décollage n'est pas autorisé avant confirmation de l'alignement.");
        }

        return Decision(
            ControllerAction.RequestClarification,
            "UNHANDLED_INTENT",
            $"{CurrentCallsign(spokenFull, spokenShort)}, précisez votre demande.",
            "Intention reconnue mais non traitée dans cet état.",
            stateBefore,
            this.session.State,
            null,
            0.4);

        ControllerDecision Speak(
            string reason,
            string text,
            TaxiRoute? route,
            double confidence,
            string? system = null)
        {
            this.session.LastControllerInstruction = text;
            return Decision(
                ControllerAction.Speak,
                reason,
                text,
                system ?? string.Empty,
                stateBefore,
                this.session.State,
                route,
                confidence);
        }

        ControllerDecision Unable(string reason, string detail)
        {
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, impossible, {detail}";
            return Decision(
                ControllerAction.Unable,
                reason,
                text,
                detail,
                stateBefore,
                this.session.State,
                null,
                0.3);
        }

        ControllerDecision Incompatible(string reason, string detail)
        {
            var text = $"{CurrentCallsign(spokenFull, spokenShort)}, impossible, {detail}";
            return Decision(
                ControllerAction.Unable,
                reason,
                text,
                detail,
                stateBefore,
                this.session.State,
                null,
                0.5);
        }
    }

    public void Observe(AirportGroundModel? airport, AircraftGroundObservation? observation)
    {
        if (airport is null || observation is null)
        {
            return;
        }

        var location = GroundLocator.Locate(airport, observation);
        this.UpdateObservedState(location, observation);
    }

    private void UpdateObservedState(GroundLocation location, AircraftGroundObservation observation)
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
                if (this.session.State is GroundSessionState.TaxiClearanceIssued or GroundSessionState.Taxiing)
                {
                    this.session.State = GroundSessionState.AtHoldShort;
                }
                break;
            case GroundPositionKind.Runway:
                if (this.session.State == GroundSessionState.LineUpCleared)
                {
                    this.session.State = GroundSessionState.LinedUp;
                }
                break;
        }
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
        double confidence) =>
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
            confidence);

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

namespace Phonie.Models;

public enum FlightSessionResetReason
{
    SimStarted,
    SimStopped,
    FlightLoaded,
    ConnectionLost,
    AircraftChanged,
    CallsignChanged,
    Manual,
}

public sealed record FlightSessionResetEvent(
    DateTimeOffset Timestamp,
    FlightSessionResetReason Reason,
    string Detail);

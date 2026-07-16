namespace AtcVfr.Models;

public enum ConnectionState
{
    Waiting,
    Connecting,
    Connected,
    Disconnected,
    Error
}

public sealed record ConnectionStatus(ConnectionState State, string Message);

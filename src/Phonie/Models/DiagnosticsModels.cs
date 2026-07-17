namespace Phonie.Models;

public sealed record DiagnosticsContext(
    bool Recording,
    string PttSource,
    string Simulator,
    double Com1Mhz,
    string Station);

public sealed record DiagnosticsSample(
    DateTimeOffset Timestamp,
    TimeSpan Uptime,
    double CpuPercent,
    double AverageCpuPercent,
    double MaximumCpuPercent,
    double WorkingSetMb,
    double MaximumWorkingSetMb,
    double ManagedMemoryMb,
    int ThreadCount,
    int HandleCount,
    double SnapshotsPerSecond,
    long TotalSnapshots,
    bool Recording,
    string PttSource,
    string Simulator,
    double Com1Mhz,
    string Station);

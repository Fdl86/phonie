namespace Phonie.Models;

public sealed record GpuAdapterInfo(
    string Name,
    long DedicatedMemoryBytes,
    string Source);

public sealed record GpuTelemetrySnapshot(
    DateTimeOffset Timestamp,
    bool Available,
    double ProcessUtilizationPercent,
    double GlobalUtilizationPercent,
    long ProcessDedicatedBytes,
    long ProcessSharedBytes,
    IReadOnlyList<int> PhysicalAdapterIndexes,
    string Status);

public sealed record GpuBenchmarkRun(
    SpeechRecognitionProfile Profile,
    int Pass,
    bool ColdStart,
    bool Success,
    string Status,
    string Transcript,
    double ModelLoadMilliseconds,
    double InferenceMilliseconds,
    double EndToEndMilliseconds,
    double ProcessGpuAveragePercent,
    double ProcessGpuMaximumPercent,
    double GlobalGpuMaximumPercent,
    long PeakDedicatedBytes,
    long PeakSharedBytes,
    double CpuAveragePercent,
    double CpuMaximumPercent,
    double PeakWorkingSetMb);

public sealed record GpuBenchmarkReport(
    string PhonieVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int ProcessId,
    string RequestedBackend,
    string BackendEvidence,
    IReadOnlyList<GpuAdapterInfo> Adapters,
    GpuTelemetrySnapshot Baseline,
    GpuTelemetrySnapshot AfterReleaseImmediate,
    GpuTelemetrySnapshot AfterReleaseThirtySeconds,
    IReadOnlyList<GpuBenchmarkRun> Runs,
    IReadOnlyList<string> Notes,
    string JsonPath,
    string TextPath);

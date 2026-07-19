namespace Phonie.Models;

public sealed record AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => this.Name;
}

public sealed record AudioRecordingResult(
    string FilePath,
    TimeSpan Duration,
    long FileSizeBytes,
    int GainDb,
    long LimitedSampleCount,
    double PeakPercent,
    bool WasDiscarded);

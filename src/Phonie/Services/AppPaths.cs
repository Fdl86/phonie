namespace Phonie.Services;

public static class AppPaths
{
    public static string BaseDirectory { get; } = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

    public static string ConfigDirectory { get; } = Path.Combine(BaseDirectory, "config");

    public static string LogsDirectory { get; } = Path.Combine(BaseDirectory, "logs");

    public static string RecordingsDirectory { get; } = Path.Combine(BaseDirectory, "recordings");

    public static string AirportDataDirectory { get; } = Path.Combine(LogsDirectory, "airport-data");

    public static string AirportDataRawDirectory { get; } = Path.Combine(AirportDataDirectory, "raw");

    public static string BenchmarksDirectory { get; } = Path.Combine(LogsDirectory, "benchmarks");

    public static string CacheDirectory { get; } = Path.Combine(BaseDirectory, "cache");

    public static string ModelsDirectory { get; } = Path.Combine(BaseDirectory, "models");

    public static string WhisperModelsDirectory { get; } = Path.Combine(ModelsDirectory, "whisper");

    public static string VoskModelsDirectory { get; } = Path.Combine(ModelsDirectory, "vosk");

    public static string SessionsDirectory { get; } = Path.Combine(LogsDirectory, "sessions");

    public static string GroundOperationsDirectory { get; } = Path.Combine(LogsDirectory, "ground-operations");

    public static string AtisCacheDirectory { get; } = Path.Combine(CacheDirectory, "atis");

    public static string ControllerVoiceCacheDirectory { get; } = Path.Combine(CacheDirectory, "controller-voice");

    public static void EnsurePortableStorage()
    {
        foreach (var directory in new[]
                 {
                     ConfigDirectory,
                     LogsDirectory,
                     AirportDataDirectory,
                     AirportDataRawDirectory,
                     BenchmarksDirectory,
                     RecordingsDirectory,
                     CacheDirectory,
                     ModelsDirectory,
                     WhisperModelsDirectory,
                     VoskModelsDirectory,
                     SessionsDirectory,
                     GroundOperationsDirectory,
                     AtisCacheDirectory,
                     ControllerVoiceCacheDirectory,
                 })
        {
            Directory.CreateDirectory(directory);
            VerifyWritable(directory);
        }
    }

    private static void VerifyWritable(string directory)
    {
        var probePath = Path.Combine(directory, $".phonie-write-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
                // The original write error, when any, is the useful one.
            }
        }
    }
}

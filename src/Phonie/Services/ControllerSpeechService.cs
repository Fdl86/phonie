using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using Phonie.Models;

namespace Phonie.Services;

public sealed record ControllerVoiceAssignment(
    string StationKey,
    string StationName,
    string VoiceName,
    string Gender,
    DateTimeOffset AssignedAt);

public sealed record ControllerVoiceInventory(
    int FrenchVoiceCount,
    int MaleCount,
    int FemaleCount,
    int OtherCount,
    IReadOnlyList<string> VoiceNames);

public sealed class ControllerSpeechService : IDisposable
{
    private readonly AudioService audioService;
    private readonly SemaphoreSlim synthesisGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, ControllerVoiceAssignment> stationVoices =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ControllerVoiceInventory inventory;
    private bool disposed;

    public ControllerSpeechService(AudioService audioService)
    {
        this.audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        inventory = ReadInventory();
    }

    public event EventHandler<string>? LogMessage;
    public event EventHandler<ControllerVoiceAssignment>? VoiceAssigned;

    public ControllerVoiceInventory Inventory => inventory;

    public IReadOnlyList<ControllerVoiceAssignment> Assignments =>
        stationVoices.Values.OrderBy(item => item.StationKey, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task SpeakControllerAsync(
        string text,
        string stationName,
        string stationKey,
        string? outputDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(outputDeviceId))
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(stationKey) ? stationName : stationKey;
        var assignment = GetOrAssignVoice(key, stationName);
        var voiceCache = string.IsNullOrWhiteSpace(assignment.VoiceName)
            ? "default"
            : assignment.VoiceName;
        var path = await EnsureSpeechAsync(
            text,
            stationName,
            voiceCache,
            Path.Combine(
                AppPaths.ControllerVoiceCacheDirectory,
                SafeSegment(key),
                SafeSegment(voiceCache)),
            rate: 0,
            cancellationToken).ConfigureAwait(false);

        if (!audioService.PlayFile(outputDeviceId, path))
        {
            PublishLog("Voix contrôleur générée mais lecture audio impossible.");
        }
    }

    public Task<string> EnsureAtisAsync(
        AtisInformation information,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(information);
        var voiceName = SelectAtisVoiceName();
        var directory = Path.Combine(
            AppPaths.AtisCacheDirectory,
            SafeSegment(information.AirportIcao),
            SafeSegment(string.IsNullOrWhiteSpace(voiceName) ? "default" : voiceName));
        return EnsureSpeechAsync(
            information.Text,
            $"{information.AirportIcao} ATIS",
            voiceName,
            directory,
            rate: -1,
            cancellationToken);
    }

    public async Task PlayAtisAsync(
        AtisInformation information,
        string? outputDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDeviceId))
        {
            return;
        }

        var path = await EnsureAtisAsync(information, cancellationToken).ConfigureAwait(false);
        if (!audioService.PlayFile(outputDeviceId, path))
        {
            PublishLog("ATIS généré mais lecture audio impossible.");
        }
    }

    public ControllerVoiceAssignment GetOrAssignVoice(string stationKey, string stationName)
    {
        var normalizedKey = NormalizeStationKey(stationKey, stationName);
        return stationVoices.GetOrAdd(normalizedKey, _ => CreateAssignment(normalizedKey, stationName));
    }

    private ControllerVoiceAssignment CreateAssignment(string stationKey, string stationName)
    {
        var voices = ReadFrenchVoices();
        if (voices.Count == 0)
        {
            var fallback = new ControllerVoiceAssignment(
                stationKey,
                stationName,
                string.Empty,
                "Système",
                DateTimeOffset.Now);
            PublishLog($"Voix station {stationName} : voix Windows par défaut (aucune voix française détectée).");
            VoiceAssigned?.Invoke(this, fallback);
            return fallback;
        }

        var male = voices.Where(item => item.Gender == VoiceGender.Male).ToArray();
        var female = voices.Where(item => item.Gender == VoiceGender.Female).ToArray();
        VoiceInfo selected;
        if (male.Length > 0 && female.Length > 0)
        {
            var pool = RandomNumberGenerator.GetInt32(2) == 0 ? male : female;
            selected = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }
        else
        {
            selected = voices[RandomNumberGenerator.GetInt32(voices.Count)];
        }

        var assignment = new ControllerVoiceAssignment(
            stationKey,
            stationName,
            selected.Name,
            FriendlyGender(selected.Gender),
            DateTimeOffset.Now);
        PublishLog($"Voix station {stationName} : {assignment.VoiceName} ({assignment.Gender}) - clé {stationKey}.");
        VoiceAssigned?.Invoke(this, assignment);
        return assignment;
    }

    private async Task<string> EnsureSpeechAsync(
        string text,
        string stationName,
        string? voiceName,
        string directory,
        int rate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Directory.CreateDirectory(directory);

        var voiceIdentity = string.IsNullOrWhiteSpace(voiceName) ? "default" : voiceName;
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes($"{stationName}|{voiceIdentity}|{rate}|{text}")))
            .ToLowerInvariant();
        var path = Path.Combine(directory, $"{hash}.wav");
        if (File.Exists(path) && new FileInfo(path).Length > 44)
        {
            return path;
        }

        await synthesisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 44)
            {
                return path;
            }

            var temporary = path + ".tmp";
            try
            {
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var synthesizer = new SpeechSynthesizer();
                    synthesizer.Rate = rate;
                    if (!string.IsNullOrWhiteSpace(voiceName))
                    {
                        synthesizer.SelectVoice(voiceName);
                    }
                    synthesizer.SetOutputToWaveFile(temporary);
                    synthesizer.Speak(text);
                    synthesizer.SetOutputToNull();
                }, cancellationToken).ConfigureAwait(false);

                File.Move(temporary, path, true);
            }
            catch
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                    // Le prochain passage nettoiera ou remplacera le fichier temporaire.
                }

                throw;
            }

            PublishLog($"Voix dynamique mise en cache : {Path.GetRelativePath(AppPaths.BaseDirectory, path)}");
            return path;
        }
        finally
        {
            synthesisGate.Release();
        }
    }

    private string SelectAtisVoiceName()
    {
        var voices = ReadFrenchVoices();
        return voices
            .OrderByDescending(item => item.Gender == VoiceGender.Female)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Name)
            .FirstOrDefault() ?? string.Empty;
    }

    private static ControllerVoiceInventory ReadInventory()
    {
        var voices = ReadFrenchVoices();
        return new ControllerVoiceInventory(
            voices.Count,
            voices.Count(item => item.Gender == VoiceGender.Male),
            voices.Count(item => item.Gender == VoiceGender.Female),
            voices.Count(item => item.Gender is not VoiceGender.Male and not VoiceGender.Female),
            voices.Select(item => item.Name).ToArray());
    }

    private static List<VoiceInfo> ReadFrenchVoices()
    {
        using var synthesizer = new SpeechSynthesizer();
        return synthesizer.GetInstalledVoices()
            .Where(item => item.Enabled
                && item.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.VoiceInfo)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeStationKey(string stationKey, string stationName)
    {
        var value = string.IsNullOrWhiteSpace(stationKey) ? stationName : stationKey;
        return value.Trim().ToUpperInvariant();
    }

    private static string FriendlyGender(VoiceGender gender) => gender switch
    {
        VoiceGender.Male => "Homme",
        VoiceGender.Female => "Femme",
        VoiceGender.Neutral => "Neutre",
        _ => "Non renseigné",
    };

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "station" : clean;
    }

    private void PublishLog(string message) => LogMessage?.Invoke(this, message);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        synthesisGate.Dispose();
    }
}

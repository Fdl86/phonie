using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using Phonie.Models;

namespace Phonie.Services;

public sealed class ControllerSpeechService : IDisposable
{
    private readonly AudioService audioService;
    private readonly SemaphoreSlim synthesisGate = new(1, 1);
    private bool disposed;

    public ControllerSpeechService(AudioService audioService)
    {
        this.audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
    }

    public event EventHandler<string>? LogMessage;

    public async Task SpeakControllerAsync(
        string text,
        string stationName,
        string? outputDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(outputDeviceId))
        {
            return;
        }

        var path = await this.EnsureSpeechAsync(
            text,
            stationName,
            Path.Combine(AppPaths.ControllerVoiceCacheDirectory, SafeSegment(stationName)),
            rate: 0,
            cancellationToken).ConfigureAwait(false);

        if (!this.audioService.PlayFile(outputDeviceId, path))
        {
            this.PublishLog("Voix contrôleur générée mais lecture audio impossible.");
        }
    }

    public Task<string> EnsureAtisAsync(
        AtisInformation information,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(information);
        var directory = Path.Combine(AppPaths.AtisCacheDirectory, SafeSegment(information.AirportIcao));
        return this.EnsureSpeechAsync(
            information.Text,
            $"{information.AirportIcao} ATIS",
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

        var path = await this.EnsureAtisAsync(information, cancellationToken).ConfigureAwait(false);
        if (!this.audioService.PlayFile(outputDeviceId, path))
        {
            this.PublishLog("ATIS généré mais lecture audio impossible.");
        }
    }

    private async Task<string> EnsureSpeechAsync(
        string text,
        string stationName,
        string directory,
        int rate,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        Directory.CreateDirectory(directory);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{stationName}|{rate}|{text}")))
            .ToLowerInvariant();
        var path = Path.Combine(directory, $"{hash}.wav");
        if (File.Exists(path) && new FileInfo(path).Length > 44)
        {
            return path;
        }

        await this.synthesisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    SelectStationVoice(synthesizer, stationName);
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
            this.PublishLog($"Voix dynamique mise en cache : {Path.GetRelativePath(AppPaths.BaseDirectory, path)}");
            return path;
        }
        finally
        {
            this.synthesisGate.Release();
        }
    }

    private static void SelectStationVoice(SpeechSynthesizer synthesizer, string stationName)
    {
        var frenchVoices = synthesizer.GetInstalledVoices()
            .Where(item => item.Enabled
                && item.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.VoiceInfo.Name)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (frenchVoices.Length == 0)
        {
            return;
        }

        var stationHash = SHA256.HashData(Encoding.UTF8.GetBytes(stationName.Trim().ToUpperInvariant()));
        var stableIndex = BitConverter.ToUInt32(stationHash, 0) % (uint)frenchVoices.Length;
        synthesizer.SelectVoice(frenchVoices[(int)stableIndex]);
    }

    private static string SafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "station" : clean;
    }

    private void PublishLog(string message) => this.LogMessage?.Invoke(this, message);

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.synthesisGate.Dispose();
    }
}

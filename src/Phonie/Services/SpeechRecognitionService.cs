using Phonie.Models;
using Whisper.net.LibraryLoader;

namespace Phonie.Services;

public sealed class SpeechRecognitionService : IDisposable
{
    private readonly WhisperService whisperService = new();
    private readonly VoskService voskService = new();
    private readonly bool startupWhisperUsesVulkan;
    private SpeechRecognitionProfile selectedProfile;
    private bool disposed;

    public SpeechRecognitionService(SpeechRecognitionProfile startupProfile)
    {
        this.selectedProfile = startupProfile;
        var startupDefinition = SpeechRecognitionProfiles.Get(startupProfile);
        this.startupWhisperUsesVulkan = startupDefinition.Backend == SpeechRecognitionBackend.Whisper
            && startupDefinition.UsesVulkan;
        if (this.startupWhisperUsesVulkan)
        {
            RuntimeOptions.RuntimeLibraryOrder =
            [
                RuntimeLibrary.Vulkan,
                RuntimeLibrary.Cpu,
            ];
        }
        else
        {
            RuntimeOptions.RuntimeLibraryOrder =
            [
                RuntimeLibrary.Cpu,
            ];
        }
        this.whisperService.StatusChanged += this.ForwardStatus;
        this.voskService.StatusChanged += this.ForwardStatus;
    }

    public event EventHandler<SpeechModelStatus>? StatusChanged;

    public SpeechRecognitionProfile SelectedProfile => this.selectedProfile;

    public bool StartupWhisperUsesVulkan => this.startupWhisperUsesVulkan;

    public bool RequiresRestart(SpeechRecognitionProfile profile) =>
        SpeechRecognitionProfiles.Get(profile).Backend == SpeechRecognitionBackend.Whisper
        && SpeechRecognitionProfiles.Get(profile).UsesVulkan != this.startupWhisperUsesVulkan;

    public bool SelectProfile(SpeechRecognitionProfile profile)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (this.RequiresRestart(profile))
        {
            return false;
        }

        var previousBackend = SpeechRecognitionProfiles.Get(this.selectedProfile).Backend;
        var nextBackend = SpeechRecognitionProfiles.Get(profile).Backend;
        this.selectedProfile = profile;

        if (previousBackend != nextBackend)
        {
            if (previousBackend == SpeechRecognitionBackend.Whisper)
            {
                this.whisperService.ReleaseModel();
            }
            else if (previousBackend == SpeechRecognitionBackend.Vosk)
            {
                this.voskService.ReleaseModel();
            }
        }

        this.StatusChanged?.Invoke(this, this.GetStatus(profile));
        return true;
    }

    public bool IsModelReady(SpeechRecognitionProfile profile)
    {
        return SpeechRecognitionProfiles.Get(profile).Backend switch
        {
            SpeechRecognitionBackend.Whisper => this.whisperService.IsModelReady(profile),
            SpeechRecognitionBackend.Vosk => this.voskService.IsModelReady,
            _ => false,
        };
    }

    public bool IsSelectedModelReady => this.IsModelReady(this.selectedProfile);

    public SpeechModelStatus GetStatus(SpeechRecognitionProfile profile)
    {
        if (this.RequiresRestart(profile))
        {
            return new SpeechModelStatus(
                profile,
                SpeechModelState.RestartRequired,
                "Profil enregistré - redémarrez PHONIE pour changer de runtime Whisper");
        }

        return SpeechRecognitionProfiles.Get(profile).Backend switch
        {
            SpeechRecognitionBackend.Whisper => this.whisperService.GetStatus(profile),
            SpeechRecognitionBackend.Vosk => this.voskService.GetStatus(),
            _ => new SpeechModelStatus(profile, SpeechModelState.Error, "Profil ASR inconnu"),
        };
    }

    public SpeechModelStatus GetSelectedStatus() => this.GetStatus(this.selectedProfile);

    public async Task DownloadSelectedModelAsync(CancellationToken cancellationToken = default)
    {
        if (this.RequiresRestart(this.selectedProfile))
        {
            throw new InvalidOperationException("Redémarrez PHONIE avant d'installer ce profil Whisper.");
        }

        if (SpeechRecognitionProfiles.Get(this.selectedProfile).Backend == SpeechRecognitionBackend.Vosk)
        {
            await this.voskService.DownloadModelAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await this.whisperService.DownloadModelAsync(this.selectedProfile, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(string audioPath, CancellationToken cancellationToken = default)
    {
        if (this.RequiresRestart(this.selectedProfile))
        {
            throw new InvalidOperationException("Redémarrez PHONIE pour activer le runtime du profil sélectionné.");
        }

        return SpeechRecognitionProfiles.Get(this.selectedProfile).Backend switch
        {
            SpeechRecognitionBackend.Whisper => await this.whisperService.TranscribeAsync(this.selectedProfile, audioPath, cancellationToken).ConfigureAwait(false),
            SpeechRecognitionBackend.Vosk => await this.voskService.TranscribeAsync(audioPath, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("Profil ASR inconnu."),
        };
    }

    public async Task<IReadOnlyList<SpeechComparisonResult>> CompareInstalledAsync(
        string audioPath,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SpeechComparisonResult>();
        var compatibleProfiles = this.startupWhisperUsesVulkan
            ? new[]
            {
                SpeechRecognitionProfile.WhisperSmallVulkan,
                SpeechRecognitionProfile.WhisperLargeV3TurboVulkan,
                SpeechRecognitionProfile.VoskFrench,
            }
            : new[]
            {
                SpeechRecognitionProfile.WhisperBaseCpu,
                SpeechRecognitionProfile.WhisperSmallCpu,
                SpeechRecognitionProfile.VoskFrench,
            };

        foreach (var profile in compatibleProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!this.IsModelReady(profile))
            {
                results.Add(new SpeechComparisonResult(profile, false, string.Empty, TimeSpan.Zero, "Modèle non installé"));
                continue;
            }

            try
            {
                SpeechTranscriptionResult transcription;
                if (profile == SpeechRecognitionProfile.VoskFrench)
                {
                    transcription = await this.voskService.TranscribeAsync(audioPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    transcription = await this.whisperService.TranscribeAsync(profile, audioPath, cancellationToken).ConfigureAwait(false);
                }

                results.Add(new SpeechComparisonResult(profile, true, transcription.NormalizedText, transcription.ProcessingTime, "OK"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new SpeechComparisonResult(profile, false, string.Empty, TimeSpan.Zero, CleanMessage(exception)));
            }
        }

        // Le laboratoire charge plusieurs moteurs successivement. Ils sont libérés après la comparaison
        // pour éviter de conserver simultanément Whisper et Vosk en mémoire pendant le vol.
        this.whisperService.ReleaseModel();
        this.voskService.ReleaseModel();
        this.StatusChanged?.Invoke(this, this.GetSelectedStatus());
        return results;
    }

    private void ForwardStatus(object? sender, SpeechModelStatus status) => this.StatusChanged?.Invoke(this, status);

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.whisperService.StatusChanged -= this.ForwardStatus;
        this.voskService.StatusChanged -= this.ForwardStatus;
        this.whisperService.Dispose();
        this.voskService.Dispose();
    }
}

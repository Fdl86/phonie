using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using NAudio.Wave;
using Phonie.Models;

namespace Phonie.Services;

public sealed class VoskService : IDisposable
{
    public const string ModelDisplayName = "Vosk Small FR 0.22";
    public const string ModelFolderName = "vosk-model-small-fr-0.22";
    public const string ExpectedArchiveSha256 = "cabf6180e177eb9b3a9a9d43a437bd5e549f3a7d09525e5d69a3fed787be12ad";

    private static readonly Uri ModelUri = new("https://alphacephei.com/vosk/models/vosk-model-small-fr-0.22.zip");
    private static readonly HttpClient HttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SemaphoreSlim operationLock = new(1, 1);
    private global::Vosk.Model? model;
    private bool disposed;

    public event EventHandler<SpeechModelStatus>? StatusChanged;

    public string ModelDirectory => Path.Combine(AppPaths.VoskModelsDirectory, ModelFolderName);

    public bool IsModelReady =>
        File.Exists(Path.Combine(this.ModelDirectory, "am", "final.mdl"))
        && File.Exists(Path.Combine(this.ModelDirectory, "conf", "model.conf"));

    public SpeechModelStatus GetStatus() => this.IsModelReady
        ? new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Ready, $"{ModelDisplayName} prêt - 41 Mio")
        : new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Missing, $"{ModelDisplayName} non installé");

    public async Task DownloadModelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var archivePath = Path.Combine(AppPaths.CacheDirectory, $"vosk-fr-{Guid.NewGuid():N}.zip");
        var extractionPath = Path.Combine(AppPaths.CacheDirectory, $"vosk-fr-{Guid.NewGuid():N}");
        try
        {
            if (this.IsModelReady)
            {
                this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Ready, $"{ModelDisplayName} déjà présent"));
                return;
            }

            Directory.CreateDirectory(AppPaths.VoskModelsDirectory);
            Directory.CreateDirectory(AppPaths.CacheDirectory);
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Downloading, $"Téléchargement de {ModelDisplayName}..."));

            using var response = await HttpClient.GetAsync(ModelUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            var buffer = new byte[1024 * 1024];
            long downloaded = 0;
            var lastPublished = DateTimeOffset.MinValue;
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                downloaded += read;
                var now = DateTimeOffset.UtcNow;
                if (now - lastPublished >= TimeSpan.FromMilliseconds(250))
                {
                    var progress = total is > 0 ? downloaded * 100.0 / total.Value : 0;
                    this.PublishStatus(new SpeechModelStatus(
                        SpeechRecognitionProfile.VoskFrench,
                        SpeechModelState.Downloading,
                        total is > 0 ? $"Téléchargement {ModelDisplayName} - {progress:F0} %" : $"Téléchargement {ModelDisplayName}...",
                        progress,
                        downloaded,
                        total));
                    lastPublished = now;
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();

            var actualHash = await ComputeSha256Async(archivePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, ExpectedArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Empreinte SHA-256 du modèle Vosk invalide : {actualHash}.");
            }

            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Loading, "Extraction sécurisée du modèle Vosk..."));
            Directory.CreateDirectory(extractionPath);
            ExtractArchiveSecurely(archivePath, extractionPath, cancellationToken);
            var finalModelFile = Directory.EnumerateFiles(extractionPath, "final.mdl", SearchOption.AllDirectories)
                .FirstOrDefault(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "am", StringComparison.OrdinalIgnoreCase));
            if (finalModelFile is null)
            {
                throw new InvalidDataException("Le modèle Vosk extrait ne contient pas am\\final.mdl.");
            }

            var amDirectory = Directory.GetParent(finalModelFile)
                              ?? throw new InvalidDataException("Structure Vosk invalide.");
            var extractedModelRoot = amDirectory.Parent?.FullName
                                     ?? throw new InvalidDataException("Racine du modèle Vosk introuvable.");
            if (!File.Exists(Path.Combine(extractedModelRoot, "conf", "model.conf")))
            {
                throw new InvalidDataException("Le modèle Vosk extrait ne contient pas conf\\model.conf.");
            }

            this.model?.Dispose();
            this.model = null;
            if (Directory.Exists(this.ModelDirectory))
            {
                Directory.Delete(this.ModelDirectory, true);
            }

            Directory.Move(extractedModelRoot, this.ModelDirectory);
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Ready, $"{ModelDisplayName} prêt - 41 Mio", 100, downloaded, total));
        }
        catch (OperationCanceledException)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Missing, "Téléchargement Vosk annulé"));
            throw;
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Error, $"Vosk : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(extractionPath);
            this.operationLock.Release();
        }
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(string inputWavPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsModelReady)
        {
            throw new DirectoryNotFoundException($"Le modèle {ModelDisplayName} doit être téléchargé avant la transcription.");
        }

        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var preparedPath = Path.Combine(AppPaths.CacheDirectory, $"vosk-{Guid.NewGuid():N}.wav");
        try
        {
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Loading, "Préparation audio 16 kHz mono..."));
            AudioPreparation.CreateMono16KhzPcmWav(inputWavPath, preparedPath);
            cancellationToken.ThrowIfCancellationRequested();

            if (this.model is null)
            {
                this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Loading, $"Chargement de {ModelDisplayName}..."));
                global::Vosk.Vosk.SetLogLevel(-1);
                this.model = new global::Vosk.Model(this.ModelDirectory);
            }

            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Transcribing, "Transcription Vosk locale en français..."));
            var result = await Task.Run(
                () => this.TranscribeCore(preparedPath, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Ready, $"{ModelDisplayName} - {result.ProcessingTime.TotalSeconds:F1} s"));
            return result;
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechRecognitionProfile.VoskFrench, SpeechModelState.Error, $"Vosk : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            TryDeleteFile(preparedPath);
            this.operationLock.Release();
        }
    }

    private SpeechTranscriptionResult TranscribeCore(string preparedPath, CancellationToken cancellationToken)
    {
        if (this.model is null)
        {
            throw new InvalidOperationException("Le modèle Vosk n'est pas chargé.");
        }

        var watch = Stopwatch.StartNew();
        using var reader = new WaveFileReader(preparedPath);
        using var recognizer = new global::Vosk.VoskRecognizer(this.model, 16_000.0f);
        recognizer.SetWords(true);
        var buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = reader.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            _ = recognizer.AcceptWaveform(buffer, read);
        }

        var json = recognizer.FinalResult();
        watch.Stop();
        var transcript = ParseText(json);
        var segments = string.IsNullOrWhiteSpace(transcript) ? Array.Empty<string>() : new[] { transcript };
        return new SpeechTranscriptionResult(
            SpeechRecognitionProfile.VoskFrench,
            transcript,
            transcript,
            watch.Elapsed,
            ModelDisplayName,
            "fr",
            segments);
    }

    private static string ParseText(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static void ExtractArchiveSecurely(string archivePath, string destinationRoot, CancellationToken cancellationToken)
    {
        var normalizedRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Archive Vosk invalide : chemin sortant du dossier cible.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
                                      ?? throw new InvalidDataException("Chemin Vosk invalide."));
            entry.ExtractToFile(destinationPath, true);
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void PublishStatus(SpeechModelStatus status) => this.StatusChanged?.Invoke(this, status);

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cache temporaire nettoyé lors d'une session ultérieure.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Cache temporaire nettoyé lors d'une session ultérieure.
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.model?.Dispose();
        this.model = null;
        this.operationLock.Dispose();
    }
}

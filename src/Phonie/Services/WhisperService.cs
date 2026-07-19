using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Phonie.Models;
using Whisper.net;

namespace Phonie.Services;

public sealed class WhisperService : IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly IReadOnlyDictionary<SpeechRecognitionProfile, WhisperModelSpec> Models =
        new Dictionary<SpeechRecognitionProfile, WhisperModelSpec>
        {
            [SpeechRecognitionProfile.WhisperBaseCpu] = new(
                "Whisper Base q5_1 multilingue",
                "ggml-base-q5_1.bin",
                new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base-q5_1.bin"),
                "422f1ae452ade6f30a004d7e5c6a43195e4433bc370bf23fac9cc591f01a8898",
                59_000_000,
                "57 Mio"),
            [SpeechRecognitionProfile.WhisperSmallCpu] = new(
                "Whisper Small q5_1 multilingue",
                "ggml-small-q5_1.bin",
                new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin"),
                "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
                185_000_000,
                "181 Mio"),
            [SpeechRecognitionProfile.WhisperSmallVulkan] = new(
                "Whisper Small q5_1 multilingue Vulkan",
                "ggml-small-q5_1.bin",
                new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin"),
                "ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",
                185_000_000,
                "181 Mio"),
        };

    private readonly SemaphoreSlim operationLock = new(1, 1);
    private WhisperFactory? factory;
    private string? loadedModelPath;
    private bool disposed;

    public event EventHandler<SpeechModelStatus>? StatusChanged;

    public bool IsModelReady(SpeechRecognitionProfile profile)
    {
        var model = GetModel(profile);
        var path = GetModelPath(profile);
        return File.Exists(path) && new FileInfo(path).Length >= model.MinimumSizeBytes;
    }

    public string GetModelPath(SpeechRecognitionProfile profile)
    {
        var model = GetModel(profile);
        return Path.Combine(AppPaths.WhisperModelsDirectory, model.FileName);
    }

    public SpeechModelStatus GetStatus(SpeechRecognitionProfile profile)
    {
        var model = GetModel(profile);
        return this.IsModelReady(profile)
            ? new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} prêt - {model.SizeLabel}")
            : new SpeechModelStatus(profile, SpeechModelState.Missing, $"{model.DisplayName} non installé");
    }

    public async Task DownloadModelAsync(SpeechRecognitionProfile profile, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var model = GetModel(profile);
        var modelPath = this.GetModelPath(profile);
        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.IsModelReady(profile))
            {
                this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} déjà présent"));
                return;
            }

            Directory.CreateDirectory(AppPaths.WhisperModelsDirectory);
            var temporaryPath = modelPath + ".download";
            TryDelete(temporaryPath);

            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Downloading, $"Téléchargement de {model.DisplayName}..."));
            using var response = await HttpClient.GetAsync(model.Uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
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
                        profile,
                        SpeechModelState.Downloading,
                        total is > 0 ? $"Téléchargement {model.DisplayName} - {progress:F0} %" : $"Téléchargement {model.DisplayName}...",
                        progress,
                        downloaded,
                        total));
                    lastPublished = now;
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();

            var actualHash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, model.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temporaryPath);
                throw new InvalidDataException($"Empreinte SHA-256 du modèle invalide : {actualHash}.");
            }

            File.Move(temporaryPath, modelPath, true);
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} prêt - {model.SizeLabel}", 100, downloaded, total));
        }
        catch (OperationCanceledException)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Missing, "Téléchargement Whisper annulé"));
            throw;
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Error, $"Whisper : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(
        SpeechRecognitionProfile profile,
        string inputWavPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var model = GetModel(profile);
        var modelPath = this.GetModelPath(profile);
        if (!this.IsModelReady(profile))
        {
            throw new FileNotFoundException($"Le modèle {model.DisplayName} doit être téléchargé avant la transcription.", modelPath);
        }

        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var preparedPath = Path.Combine(AppPaths.CacheDirectory, $"whisper-{Guid.NewGuid():N}.wav");
        try
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Loading, "Préparation audio 16 kHz mono..."));
            AudioPreparation.CreateMono16KhzPcmWav(inputWavPath, preparedPath);
            cancellationToken.ThrowIfCancellationRequested();

            if (this.factory is null || !string.Equals(this.loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                this.factory?.Dispose();
                this.factory = null;
                this.loadedModelPath = null;
                this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Loading, $"Chargement de {model.DisplayName}..."));
                this.factory = WhisperFactory.FromPath(modelPath);
                this.loadedModelPath = modelPath;
            }

            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Transcribing, "Transcription locale en français..."));
            var watch = Stopwatch.StartNew();
            var segments = new List<string>();
            using var processor = this.factory.CreateBuilder()
                .WithLanguage("fr")
                .Build();
            await using var stream = File.OpenRead(preparedPath);
            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = segment.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
            }

            watch.Stop();
            var raw = string.Join(" ", segments).Trim();
            var normalized = NormalizeWhitespace(raw);
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} - {watch.Elapsed.TotalSeconds:F1} s"));
            return new SpeechTranscriptionResult(profile, raw, normalized, watch.Elapsed, model.DisplayName, "fr", segments);
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Error, $"Transcription : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            TryDelete(preparedPath);
            this.operationLock.Release();
        }
    }

    private static WhisperModelSpec GetModel(SpeechRecognitionProfile profile)
    {
        if (!Models.TryGetValue(profile, out var model))
        {
            throw new ArgumentOutOfRangeException(nameof(profile), profile, "Ce profil n'utilise pas Whisper.");
        }

        return model;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void PublishStatus(SpeechModelStatus status) => this.StatusChanged?.Invoke(this, status);

    private static string NormalizeWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var character in text.Trim())
        {
            var isSpace = char.IsWhiteSpace(character);
            if (isSpace && previousWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : character);
            previousWasSpace = isSpace;
        }

        return builder.ToString();
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private static void TryDelete(string path)
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
            // Le cache sera nettoyé lors d'une prochaine session.
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.factory?.Dispose();
        this.factory = null;
        this.operationLock.Dispose();
    }

    private sealed record WhisperModelSpec(
        string DisplayName,
        string FileName,
        Uri Uri,
        string Sha256,
        long MinimumSizeBytes,
        string SizeLabel);
}

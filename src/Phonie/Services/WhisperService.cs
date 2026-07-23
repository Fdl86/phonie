using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
            [SpeechRecognitionProfile.WhisperLargeV3TurboVulkan] = new(
                "Whisper Large-v3 Turbo q5_0 multilingue Vulkan",
                "ggml-large-v3-turbo-q5_0.bin",
                new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin"),
                "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
                570_000_000,
                "548 Mio"),
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

    public Task<SpeechTranscriptionResult> TranscribeAsync(
        SpeechRecognitionProfile profile,
        string inputWavPath,
        CancellationToken cancellationToken = default) =>
        this.TranscribeAsync(profile, inputWavPath, null, cancellationToken);

    public async Task<SpeechTranscriptionResult> TranscribeAsync(
        SpeechRecognitionProfile profile,
        string inputWavPath,
        SpeechRecognitionContext? context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var model = GetModel(profile);
        var modelPath = this.GetModelPath(profile);
        if (!this.IsModelReady(profile))
        {
            throw new FileNotFoundException($"Le modèle {model.DisplayName} doit être téléchargé avant la transcription.", modelPath);
        }

        var totalWatch = Stopwatch.StartNew();
        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var preparedPath = Path.Combine(AppPaths.CacheDirectory, $"whisper-{Guid.NewGuid():N}.wav");
        try
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Loading, "Préparation audio 16 kHz mono..."));
            AudioPreparation.CreateMono16KhzPcmWav(inputWavPath, preparedPath);
            cancellationToken.ThrowIfCancellationRequested();
            var signal = InspectPreparedWave(preparedPath);
            if (signal.Duration < TimeSpan.FromMilliseconds(180) || signal.Dbfs < -62.0)
            {
                totalWatch.Stop();
                var rejection = signal.Duration < TimeSpan.FromMilliseconds(180)
                    ? "PTT trop court pour une transmission exploitable."
                    : "Niveau audio insuffisant ou PTT vide.";
                this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, rejection));
                return new SpeechTranscriptionResult(profile, string.Empty, string.Empty, TimeSpan.Zero, model.DisplayName, "fr", Array.Empty<string>())
                {
                    EndToEndTime = totalWatch.Elapsed,
                    Prompt = context?.Prompt ?? string.Empty,
                    WasRejected = true,
                    RejectionReason = rejection,
                    SignalDuration = signal.Duration,
                    SignalDbfs = signal.Dbfs,
                };
            }

            var coldModelLoad = this.factory is null
                || !string.Equals(this.loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase);
            var modelLoadTime = TimeSpan.Zero;
            if (coldModelLoad)
            {
                this.factory?.Dispose();
                this.factory = null;
                this.loadedModelPath = null;
                this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Loading, $"Chargement de {model.DisplayName}..."));
                var loadWatch = Stopwatch.StartNew();
                this.factory = WhisperFactory.FromPath(modelPath);
                loadWatch.Stop();
                modelLoadTime = loadWatch.Elapsed;
                this.loadedModelPath = modelPath;
            }

            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Transcribing, "Transcription locale en français..."));
            var factory = this.factory ?? throw new InvalidOperationException("Le moteur Whisper n'est pas chargé.");
            var watch = Stopwatch.StartNew();
            var segments = new List<string>();
            var probabilities = new List<double>();
            var builder = factory.CreateBuilder()
                .WithLanguage("fr")
                .WithNoContext()
                .WithSingleSegment()
                .WithNoSpeechThreshold(0.60f)
                .WithProbabilities();
            if (!string.IsNullOrWhiteSpace(context?.Prompt))
            {
                builder.WithPrompt(context.Prompt);
            }
            using var processor = builder.Build();
            await using var stream = File.OpenRead(preparedPath);
            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = segment.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    segments.Add(text);
                }
                var probability = ReadSegmentProbability(segment);
                if (double.IsFinite(probability) && probability > 0)
                {
                    probabilities.Add(probability);
                }
            }

            watch.Stop();
            totalWatch.Stop();
            var raw = string.Join(" ", segments).Trim();
            var normalized = SanitizeTranscript(raw, context?.Prompt, out var rejectionReason);
            var rejected = !string.IsNullOrWhiteSpace(rejectionReason);
            if (rejected)
            {
                normalized = string.Empty;
            }
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} - {watch.Elapsed.TotalSeconds:F1} s"));
            return new SpeechTranscriptionResult(profile, raw, normalized, watch.Elapsed, model.DisplayName, "fr", segments)
            {
                ModelLoadTime = modelLoadTime,
                EndToEndTime = totalWatch.Elapsed,
                ColdModelLoad = coldModelLoad,
                Prompt = context?.Prompt ?? string.Empty,
                AverageProbability = probabilities.Count == 0 ? 0 : probabilities.Average(),
                WasRejected = rejected,
                RejectionReason = rejectionReason,
                SignalDuration = signal.Duration,
                SignalDbfs = signal.Dbfs,
            };
        }
        catch (OperationCanceledException)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, $"{model.DisplayName} - opération annulée"));
            throw;
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

    public async Task<SpeechTranscriptionResult> WarmUpAsync(
        SpeechRecognitionProfile profile,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!SpeechRecognitionProfiles.Get(profile).UsesVulkan)
        {
            throw new InvalidOperationException("Le préchauffage est réservé aux profils Whisper Vulkan.");
        }

        var warmupPath = Path.Combine(AppPaths.CacheDirectory, $"whisper-warmup-{Guid.NewGuid():N}.wav");
        try
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.WarmingUp, "Initialisation du moteur qualité Vulkan..."));
            WriteSilenceWave(warmupPath, TimeSpan.FromMilliseconds(800));
            var result = await this.TranscribeAsync(profile, warmupPath, cancellationToken).ConfigureAwait(false);
            this.PublishStatus(new SpeechModelStatus(
                profile,
                SpeechModelState.Ready,
                $"Moteur qualité prêt - initialisation {result.EndToEndTime.TotalSeconds:F1} s"));
            return result;
        }
        catch (OperationCanceledException)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Ready, "Initialisation Vulkan annulée"));
            throw;
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(profile, SpeechModelState.Error, $"Préchauffage Vulkan : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            TryDelete(warmupPath);
        }
    }

    private static (TimeSpan Duration, double Dbfs) InspectPreparedWave(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length <= 44)
        {
            return (TimeSpan.Zero, double.NegativeInfinity);
        }

        const int sampleRate = 16_000;
        var sampleCount = (bytes.Length - 44) / 2;
        if (sampleCount <= 0)
        {
            return (TimeSpan.Zero, double.NegativeInfinity);
        }

        double sumSquares = 0;
        for (var offset = 44; offset + 1 < bytes.Length; offset += 2)
        {
            var sample = BitConverter.ToInt16(bytes, offset) / 32768.0;
            sumSquares += sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        var dbfs = rms <= 0 ? double.NegativeInfinity : 20.0 * Math.Log10(rms);
        return (TimeSpan.FromSeconds(sampleCount / (double)sampleRate), dbfs);
    }

    private static double ReadSegmentProbability(object segment)
    {
        try
        {
            var property = segment.GetType().GetProperty("Probability");
            var value = property?.GetValue(segment);
            return value is null ? 0 : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static string SanitizeTranscript(string raw, string? prompt, out string rejectionReason)
    {
        rejectionReason = string.Empty;
        var text = NormalizeWhitespace(raw).Trim('"', '\'', '“', '”', '«', '»').Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            rejectionReason = "Aucune parole reconnue.";
            return string.Empty;
        }

        if (Regex.IsMatch(text, @"^\s*(?:\*[^*]+\*|\[[^\]]+\]|\([^\)]+\))\s*$", RegexOptions.CultureInvariant))
        {
            rejectionReason = "Description de bruit ou de non-parole rejetée.";
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            var normalizedText = NormalizeForComparison(text);
            var normalizedPrompt = NormalizeForComparison(prompt);
            var tokens = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var promptTokens = normalizedPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var overlap = tokens.Length == 0 ? 0 : tokens.Count(promptTokens.Contains) / (double)tokens.Length;
            if ((tokens.Length >= 8 && overlap >= 0.88)
                || normalizedText.Contains("phraseologie atc francaise", StringComparison.Ordinal)
                || normalizedText.StartsWith("station indicatif", StringComparison.Ordinal))
            {
                rejectionReason = "Fragment du prompt dynamique rejeté.";
                return string.Empty;
            }
        }

        return text;
    }

    private static string NormalizeForComparison(string value)
    {
        var formD = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var character in formD)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }
        return NormalizeWhitespace(builder.ToString());
    }

    private static void WriteSilenceWave(string path, TimeSpan duration)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = Math.Max(1, (int)Math.Round(sampleRate * duration.TotalSeconds));
        var dataLength = sampleCount * channels * bitsPerSample / 8;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppPaths.CacheDirectory);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = (short)Math.Round(Math.Sin(2.0 * Math.PI * 220.0 * index / sampleRate) * 600.0);
            writer.Write(sample);
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

    public void ReleaseModel()
    {
        if (this.disposed)
        {
            return;
        }

        this.factory?.Dispose();
        this.factory = null;
        this.loadedModelPath = null;
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

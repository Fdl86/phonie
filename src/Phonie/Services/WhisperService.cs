using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Phonie.Models;
using Whisper.net;

namespace Phonie.Services;

public sealed class WhisperService : IDisposable
{
    public const string ModelDisplayName = "Whisper Small q5_1 multilingue";
    public const string ModelFileName = "ggml-small-q5_1.bin";
    public const long ExpectedModelSizeBytes = 190_000_000;
    public const string ExpectedSha1 = "6fe57ddcfdd1c6b07cdcc73aaf620810ce5fc771";

    private static readonly Uri ModelUri = new("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small-q5_1.bin");
    private static readonly HttpClient HttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SemaphoreSlim operationLock = new(1, 1);
    private WhisperFactory? factory;
    private bool disposed;

    public event EventHandler<SpeechModelStatus>? StatusChanged;

    public string ModelPath => Path.Combine(AppPaths.WhisperModelsDirectory, ModelFileName);

    public bool IsModelReady => File.Exists(this.ModelPath) && new FileInfo(this.ModelPath).Length > 170_000_000;

    public SpeechModelStatus GetStatus() => this.IsModelReady
        ? new SpeechModelStatus(SpeechModelState.Ready, $"{ModelDisplayName} prêt - 181 Mio")
        : new SpeechModelStatus(SpeechModelState.Missing, "Modèle Whisper Small q5_1 non installé");

    public async Task DownloadModelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this.IsModelReady)
            {
                this.PublishStatus(new SpeechModelStatus(SpeechModelState.Ready, $"{ModelDisplayName} déjà présent"));
                return;
            }

            Directory.CreateDirectory(AppPaths.WhisperModelsDirectory);
            var temporaryPath = this.ModelPath + ".download";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Downloading, "Téléchargement de Whisper Small q5_1..."));
            using var response = await HttpClient.GetAsync(ModelUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            var buffer = new byte[1024 * 1024];
            long downloaded = 0;
            var lastPublished = DateTimeOffset.MinValue;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
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
                        SpeechModelState.Downloading,
                        total is > 0 ? $"Téléchargement Whisper Small q5_1 - {progress:F0} %" : "Téléchargement Whisper Small q5_1...",
                        progress,
                        downloaded,
                        total));
                    lastPublished = now;
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Close();

            var actualHash = await ComputeSha1Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, ExpectedSha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException($"Empreinte du modèle invalide : {actualHash}.");
            }

            File.Move(temporaryPath, this.ModelPath, true);
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Ready, $"{ModelDisplayName} prêt - 181 Mio", 100, downloaded, total));
        }
        catch (OperationCanceledException)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Missing, "Téléchargement Whisper annulé"));
            throw;
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Error, $"Whisper : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            this.operationLock.Release();
        }
    }

    public async Task<SpeechTranscriptionResult> TranscribeAsync(string inputWavPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        if (!this.IsModelReady)
        {
            throw new FileNotFoundException("Le modèle Whisper Small q5_1 doit être téléchargé avant la transcription.", this.ModelPath);
        }

        await this.operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var preparedPath = Path.Combine(AppPaths.CacheDirectory, $"whisper-{Guid.NewGuid():N}.wav");
        try
        {
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Loading, "Préparation audio 16 kHz mono..."));
            PrepareAudio(inputWavPath, preparedPath);
            cancellationToken.ThrowIfCancellationRequested();

            if (this.factory is null)
            {
                this.PublishStatus(new SpeechModelStatus(SpeechModelState.Loading, "Chargement de Whisper Small q5_1..."));
                this.factory = WhisperFactory.FromPath(this.ModelPath);
            }

            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Transcribing, "Transcription locale en français..."));
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
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Ready, $"Transcription terminée en {watch.Elapsed.TotalSeconds:F1} s"));
            return new SpeechTranscriptionResult(raw, normalized, watch.Elapsed, ModelDisplayName, "fr", segments);
        }
        catch (Exception exception)
        {
            this.PublishStatus(new SpeechModelStatus(SpeechModelState.Error, $"Transcription : {CleanMessage(exception)}"));
            throw;
        }
        finally
        {
            try
            {
                if (File.Exists(preparedPath))
                {
                    File.Delete(preparedPath);
                }
            }
            catch
            {
                // Le cache sera nettoyé lors d'une prochaine session.
            }

            this.operationLock.Release();
        }
    }

    private static void PrepareAudio(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(AppPaths.CacheDirectory);
        using var reader = new AudioFileReader(sourcePath);
        ISampleProvider mono = reader.WaveFormat.Channels switch
        {
            1 => reader,
            2 => new StereoToMonoSampleProvider(reader) { LeftVolume = 0.5f, RightVolume = 0.5f },
            _ => new ChannelToMonoSampleProvider(reader),
        };
        var resampled = new WdlResamplingSampleProvider(mono, 16_000);
        WaveFileWriter.CreateWaveFile16(destinationPath, resampled);
    }

    private static async Task<string> ComputeSha1Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha1 = SHA1.Create();
        var hash = await sha1.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
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

    private sealed class ChannelToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly float[] sourceBuffer;

        public ChannelToMonoSampleProvider(ISampleProvider source)
        {
            this.source = source;
            this.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
            this.sourceBuffer = new float[source.WaveFormat.Channels * 4096];
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var channels = this.source.WaveFormat.Channels;
            var requested = Math.Min(count * channels, this.sourceBuffer.Length);
            var read = this.source.Read(this.sourceBuffer, 0, requested);
            var frames = read / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0.0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += this.sourceBuffer[(frame * channels) + channel];
                }

                buffer[offset + frame] = sum / channels;
            }

            return frames;
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phonie.Models;

namespace Phonie.Services;

public sealed class GpuBenchmarkService
{
    private static readonly SpeechRecognitionProfile[] BenchmarkProfiles =
    [
        SpeechRecognitionProfile.WhisperSmallVulkan,
        SpeechRecognitionProfile.WhisperLargeV3TurboVulkan,
        SpeechRecognitionProfile.VoskFrench,
    ];

    public async Task<GpuBenchmarkReport> RunAsync(
        string audioPath,
        SpeechRecognitionService speechRecognitionService,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioPath);
        ArgumentNullException.ThrowIfNull(speechRecognitionService);
        if (!File.Exists(audioPath))
        {
            throw new FileNotFoundException("Le WAV de référence est introuvable.", audioPath);
        }

        if (!speechRecognitionService.StartupWhisperUsesVulkan)
        {
            throw new InvalidOperationException("Le benchmark GPU demande un démarrage de PHONIE avec un profil Whisper Vulkan.");
        }

        var startedAt = DateTimeOffset.Now;
        var processId = Environment.ProcessId;
        var notes = new List<string>
        {
            "Utilisation GPU processus : maximum des moteurs GPU Windows associés au PID PHONIE.",
            "VRAM dédiée et mémoire GPU partagée : compteurs Windows GPU Process Memory associés au PID PHONIE.",
            "La VRAM totale des adaptateurs provient du registre Windows et peut être indisponible selon le pilote.",
            "Les profils absents sont signalés sans téléchargement automatique.",
        };

        progress?.Report("Préparation du benchmark : libération des modèles...");
        speechRecognitionService.ReleaseAllModels();
        ForceManagedCleanup();
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        using var telemetry = new GpuTelemetryService();
        notes.Add(telemetry.Status);
        await telemetry.PrimeAsync(cancellationToken).ConfigureAwait(false);
        var baseline = telemetry.Read();
        var runs = new List<GpuBenchmarkRun>();

        foreach (var profile in BenchmarkProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var definition = SpeechRecognitionProfiles.Get(profile);
            if (!speechRecognitionService.IsRuntimeCompatible(profile))
            {
                runs.Add(CreateSkippedRun(profile, "Runtime incompatible avec cette session"));
                continue;
            }

            if (!speechRecognitionService.IsModelReady(profile))
            {
                runs.Add(CreateSkippedRun(profile, "Modèle non installé"));
                continue;
            }

            progress?.Report($"{definition.ShortName} : préparation du passage à froid...");
            speechRecognitionService.ReleaseAllModels();
            ForceManagedCleanup();
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);

            for (var pass = 1; pass <= 3; pass++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"{definition.ShortName} : passage {pass}/3 {(pass == 1 ? "à froid" : "à chaud")}...");
                var measured = await MeasureOperationAsync(
                    telemetry,
                    () => speechRecognitionService.TranscribeProfileAsync(profile, audioPath, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (measured.Result is not null)
                {
                    runs.Add(new GpuBenchmarkRun(
                        profile,
                        pass,
                        pass == 1,
                        true,
                        "OK",
                        measured.Result.NormalizedText,
                        measured.Result.ModelLoadTime.TotalMilliseconds,
                        measured.Result.ProcessingTime.TotalMilliseconds,
                        measured.Result.EndToEndTime.TotalMilliseconds,
                        measured.ProcessGpuAveragePercent,
                        measured.ProcessGpuMaximumPercent,
                        measured.GlobalGpuMaximumPercent,
                        measured.PeakDedicatedBytes,
                        measured.PeakSharedBytes,
                        measured.CpuAveragePercent,
                        measured.CpuMaximumPercent,
                        measured.PeakWorkingSetMb));
                }
                else
                {
                    runs.Add(new GpuBenchmarkRun(
                        profile,
                        pass,
                        pass == 1,
                        false,
                        measured.ErrorMessage ?? "Erreur inconnue",
                        string.Empty,
                        0,
                        0,
                        measured.Elapsed.TotalMilliseconds,
                        measured.ProcessGpuAveragePercent,
                        measured.ProcessGpuMaximumPercent,
                        measured.GlobalGpuMaximumPercent,
                        measured.PeakDedicatedBytes,
                        measured.PeakSharedBytes,
                        measured.CpuAveragePercent,
                        measured.CpuMaximumPercent,
                        measured.PeakWorkingSetMb));
                }
            }
        }

        progress?.Report("Libération des moteurs et mesure immédiate de la VRAM...");
        speechRecognitionService.ReleaseAllModels();
        ForceManagedCleanup();
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        var afterReleaseImmediate = telemetry.Read();

        for (var remaining = 30; remaining > 0; remaining--)
        {
            if (remaining == 30 || remaining % 5 == 0)
            {
                progress?.Report($"Mesure de la VRAM résiduelle : encore {remaining} s...");
            }

            _ = telemetry.Read();
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
        }

        var afterReleaseThirtySeconds = telemetry.Read();
        var successfulGpuRuns = runs
            .Where(run => run.Success
                && run.Profile is SpeechRecognitionProfile.WhisperSmallVulkan
                    or SpeechRecognitionProfile.WhisperLargeV3TurboVulkan)
            .ToArray();
        var gpuConfirmed = successfulGpuRuns.Any(run =>
            run.ProcessGpuMaximumPercent >= 1.0
            || run.PeakDedicatedBytes >= baseline.ProcessDedicatedBytes + 32L * 1024 * 1024);
        var backendEvidence = gpuConfirmed
            ? "Vulkan confirmé par l'activité GPU ou la VRAM du processus PHONIE"
            : "Vulkan demandé, mais activité GPU du processus non confirmée ; fallback CPU possible ou compteurs indisponibles";

        var completedAt = DateTimeOffset.Now;
        Directory.CreateDirectory(AppPaths.BenchmarksDirectory);
        var stem = $"gpu-benchmark-{completedAt:yyyyMMdd-HHmmss}";
        var jsonPath = Path.Combine(AppPaths.BenchmarksDirectory, stem + ".json");
        var textPath = Path.Combine(AppPaths.BenchmarksDirectory, stem + ".txt");
        var report = new GpuBenchmarkReport(
            "DEV0.4.0.2",
            startedAt,
            completedAt,
            processId,
            "Whisper Vulkan puis CPU",
            backendEvidence,
            telemetry.Adapters,
            baseline,
            afterReleaseImmediate,
            afterReleaseThirtySeconds,
            runs,
            notes,
            jsonPath,
            textPath);

        progress?.Report("Écriture du rapport GPU/VRAM...");
        await WriteReportAsync(report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private static GpuBenchmarkRun CreateSkippedRun(SpeechRecognitionProfile profile, string status) =>
        new(
            profile,
            0,
            false,
            false,
            status,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

    private static async Task<MeasuredOperation> MeasureOperationAsync(
        GpuTelemetryService telemetry,
        Func<Task<SpeechTranscriptionResult>> operation,
        CancellationToken cancellationToken)
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var operationWatch = Stopwatch.StartNew();
        var initialCpu = process.TotalProcessorTime;
        var previousCpu = initialCpu;
        var previousElapsed = TimeSpan.Zero;
        var cpuMaximum = 0.0;
        var peakWorkingSetMb = process.WorkingSet64 / 1024.0 / 1024.0;
        var telemetrySamples = new ConcurrentQueue<GpuTelemetrySnapshot>();
        telemetrySamples.Enqueue(telemetry.Read());

        using var sampleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sampleTask = Task.Run(async () =>
        {
            try
            {
                while (!sampleCancellation.Token.IsCancellationRequested)
                {
                    await Task.Delay(100, sampleCancellation.Token).ConfigureAwait(false);
                    telemetrySamples.Enqueue(telemetry.Read());
                    process.Refresh();
                    peakWorkingSetMb = Math.Max(peakWorkingSetMb, process.WorkingSet64 / 1024.0 / 1024.0);
                    var elapsed = operationWatch.Elapsed;
                    var currentCpu = process.TotalProcessorTime;
                    var intervalSeconds = Math.Max(0.001, (elapsed - previousElapsed).TotalSeconds);
                    var cpuSeconds = Math.Max(0, (currentCpu - previousCpu).TotalSeconds);
                    var intervalCpu = cpuSeconds / intervalSeconds / Math.Max(1, Environment.ProcessorCount) * 100.0;
                    cpuMaximum = Math.Max(cpuMaximum, Math.Clamp(intervalCpu, 0, 100));
                    previousCpu = currentCpu;
                    previousElapsed = elapsed;
                }
            }
            catch (OperationCanceledException) when (sampleCancellation.IsCancellationRequested)
            {
                // Fin normale de l'échantillonnage du passage.
            }
        }, CancellationToken.None);

        SpeechTranscriptionResult? result = null;
        string? errorMessage = null;
        try
        {
            result = await operation().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errorMessage = CleanMessage(exception);
        }
        finally
        {
            operationWatch.Stop();
            sampleCancellation.Cancel();
            try
            {
                await sampleTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Fin normale.
            }

            telemetrySamples.Enqueue(telemetry.Read());
            process.Refresh();
            peakWorkingSetMb = Math.Max(peakWorkingSetMb, process.WorkingSet64 / 1024.0 / 1024.0);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var elapsedSeconds = Math.Max(0.001, operationWatch.Elapsed.TotalSeconds);
        var cpuAverage = Math.Clamp(
            (process.TotalProcessorTime - initialCpu).TotalSeconds
            / elapsedSeconds
            / Math.Max(1, Environment.ProcessorCount)
            * 100.0,
            0,
            100);
        var samples = telemetrySamples.ToArray();
        var processGpuSamples = samples.Where(sample => sample.Available).Select(sample => sample.ProcessUtilizationPercent).ToArray();
        return new MeasuredOperation(
            result,
            errorMessage,
            operationWatch.Elapsed,
            processGpuSamples.Length == 0 ? 0 : processGpuSamples.Average(),
            processGpuSamples.Length == 0 ? 0 : processGpuSamples.Max(),
            samples.Length == 0 ? 0 : samples.Max(sample => sample.GlobalUtilizationPercent),
            samples.Length == 0 ? 0 : samples.Max(sample => sample.ProcessDedicatedBytes),
            samples.Length == 0 ? 0 : samples.Max(sample => sample.ProcessSharedBytes),
            cpuAverage,
            Math.Max(cpuMaximum, cpuAverage),
            peakWorkingSetMb);
    }

    private static async Task WriteReportAsync(GpuBenchmarkReport report, CancellationToken cancellationToken)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(report, jsonOptions);
        await File.WriteAllTextAsync(report.JsonPath, json, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(report.TextPath, BuildTextReport(report), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static string BuildTextReport(GpuBenchmarkReport report)
    {
        var culture = CultureInfo.InvariantCulture;
        var builder = new StringBuilder();
        builder.AppendLine("PHONIE DEV0.4.0.2 - BENCHMARK GPU / VRAM");
        builder.AppendLine($"Début : {report.StartedAt:O}");
        builder.AppendLine($"Fin : {report.CompletedAt:O}");
        builder.AppendLine($"PID : {report.ProcessId}");
        builder.AppendLine($"Backend demandé : {report.RequestedBackend}");
        builder.AppendLine($"Preuve backend : {report.BackendEvidence}");
        builder.AppendLine();
        builder.AppendLine("ADAPTATEURS DÉTECTÉS");
        if (report.Adapters.Count == 0)
        {
            builder.AppendLine("- Métadonnées adaptateur indisponibles");
        }
        else
        {
            foreach (var adapter in report.Adapters)
            {
                builder.AppendLine($"- {adapter.Name} - VRAM déclarée {FormatBytes(adapter.DedicatedMemoryBytes)} - {adapter.Source}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("MÉMOIRE GPU PROCESSUS");
        AppendSnapshot(builder, "Avant test", report.Baseline);
        AppendSnapshot(builder, "Après libération immédiate", report.AfterReleaseImmediate);
        AppendSnapshot(builder, "Après libération + 30 s", report.AfterReleaseThirtySeconds);

        builder.AppendLine();
        builder.AppendLine("PASSAGES");
        foreach (var group in report.Runs.GroupBy(run => run.Profile))
        {
            builder.AppendLine($"[{SpeechRecognitionProfiles.Get(group.Key).ShortName}]");
            foreach (var run in group)
            {
                if (run.Pass == 0)
                {
                    builder.AppendLine($"  Non exécuté : {run.Status}");
                    continue;
                }

                builder.AppendLine(
                    $"  Passage {run.Pass} {(run.ColdStart ? "froid" : "chaud")} - " +
                    $"chargement {run.ModelLoadMilliseconds.ToString("F0", culture)} ms - " +
                    $"inférence {run.InferenceMilliseconds.ToString("F0", culture)} ms - " +
                    $"bout en bout {run.EndToEndMilliseconds.ToString("F0", culture)} ms");
                builder.AppendLine(
                    $"    GPU processus moy/max {run.ProcessGpuAveragePercent.ToString("F1", culture)} / {run.ProcessGpuMaximumPercent.ToString("F1", culture)} % - " +
                    $"GPU global max {run.GlobalGpuMaximumPercent.ToString("F1", culture)} %");
                builder.AppendLine(
                    $"    VRAM pic {FormatBytes(run.PeakDedicatedBytes)} - partagée {FormatBytes(run.PeakSharedBytes)} - " +
                    $"RAM PHONIE pic {run.PeakWorkingSetMb.ToString("F1", culture)} Mio - " +
                    $"CPU moy/max {run.CpuAveragePercent.ToString("F1", culture)} / {run.CpuMaximumPercent.ToString("F1", culture)} %");
                builder.AppendLine($"    Statut : {run.Status}");
                if (!string.IsNullOrWhiteSpace(run.Transcript))
                {
                    builder.AppendLine($"    Texte : {run.Transcript.ReplaceLineEndings(" ").Trim()}");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("NOTES");
        foreach (var note in report.Notes)
        {
            builder.AppendLine($"- {note}");
        }

        return builder.ToString();
    }

    private static void AppendSnapshot(StringBuilder builder, string label, GpuTelemetrySnapshot snapshot)
    {
        builder.AppendLine(
            $"- {label} : VRAM {FormatBytes(snapshot.ProcessDedicatedBytes)} - " +
            $"partagée {FormatBytes(snapshot.ProcessSharedBytes)} - " +
            $"GPU processus {snapshot.ProcessUtilizationPercent:F1} % - {snapshot.Status}");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 Mio";
        }

        return bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024 / 1024:F2} Gio"
            : $"{bytes / 1024.0 / 1024:F1} Mio";
    }

    private static void ForceManagedCleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private sealed record MeasuredOperation(
        SpeechTranscriptionResult? Result,
        string? ErrorMessage,
        TimeSpan Elapsed,
        double ProcessGpuAveragePercent,
        double ProcessGpuMaximumPercent,
        double GlobalGpuMaximumPercent,
        long PeakDedicatedBytes,
        long PeakSharedBytes,
        double CpuAveragePercent,
        double CpuMaximumPercent,
        double PeakWorkingSetMb);
}

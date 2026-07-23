using System.Diagnostics;
using System.Globalization;
using System.Text;
using Phonie.Models;

namespace Phonie.Services;

/// <summary>
/// Produces one compact, shareable diagnostics log per PHONIE session.
/// Sampling is deliberately slow (5 seconds) and file writes are tiny.
/// </summary>
public sealed class DiagnosticsService : IAsyncDisposable
{
    private const int MaximumLogFiles = 10;
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(5);

    private readonly object syncRoot = new();
    private readonly Process process = Process.GetCurrentProcess();
    private readonly StreamWriter writer;
    private readonly Stopwatch uptime = Stopwatch.StartNew();
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private Func<DiagnosticsContext>? contextProvider;
    private TimeSpan previousProcessorTime;
    private TimeSpan previousSampleUptime;
    private long totalSnapshots;
    private long snapshotsAtPreviousSample;
    private int pttCount;
    private double totalPttSeconds;
    private double cpuSum;
    private int cpuSampleCount;
    private double maximumCpu;
    private double maximumWorkingSetMb;
    private bool disposed;

    public DiagnosticsService()
    {
        this.DirectoryPath = AppPaths.LogsDirectory;
        Directory.CreateDirectory(this.DirectoryPath);
        RotateLogs(this.DirectoryPath, MaximumLogFiles - 1);

        this.SessionFilePath = Path.Combine(this.DirectoryPath, $"PHONIE-DEV0.4.2.0-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        this.writer = new StreamWriter(this.SessionFilePath, false, new UTF8Encoding(false))
        {
            AutoFlush = true,
        };

        this.previousProcessorTime = this.process.TotalProcessorTime;
        this.previousSampleUptime = this.uptime.Elapsed;
        this.WriteHeader();
    }

    public string DirectoryPath { get; }

    public string SessionFilePath { get; }

    public event EventHandler<DiagnosticsSample>? SampleAvailable;

    public void Start(Func<DiagnosticsContext> contextProvider)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        ArgumentNullException.ThrowIfNull(contextProvider);

        if (this.worker is { IsCompleted: false })
        {
            return;
        }

        this.contextProvider = contextProvider;
        this.cancellation = new CancellationTokenSource();
        this.worker = Task.Run(() => this.RunAsync(this.cancellation.Token));
        this.WriteEvent("SESSION", "Démarrage du suivi de légèreté - échantillon toutes les 5 secondes.");
    }

    public void ReportSnapshot() => Interlocked.Increment(ref this.totalSnapshots);

    public void ReportPttCompleted(TimeSpan duration)
    {
        lock (this.syncRoot)
        {
            this.pttCount++;
            this.totalPttSeconds += Math.Max(0, duration.TotalSeconds);
        }
    }

    public void WriteEvent(string category, string message)
    {
        if (this.disposed)
        {
            return;
        }

        var cleanCategory = CleanField(category).ToUpperInvariant();
        var cleanMessage = CleanField(message);
        lock (this.syncRoot)
        {
            this.writer.WriteLine($"{DateTimeOffset.Now:O}\tEVENT\t{cleanCategory}\t{cleanMessage}");
        }
    }

    public void Mark(string message) => this.WriteEvent("MARK", message);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SampleInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var sample = this.CreateSample();
                this.WriteSample(sample);
                this.SampleAvailable?.Invoke(this, sample);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private DiagnosticsSample CreateSample()
    {
        this.process.Refresh();

        var nowUptime = this.uptime.Elapsed;
        var processorTime = this.process.TotalProcessorTime;
        var elapsedSeconds = Math.Max(0.001, (nowUptime - this.previousSampleUptime).TotalSeconds);
        var processorSeconds = Math.Max(0, (processorTime - this.previousProcessorTime).TotalSeconds);
        var cpuPercent = Math.Clamp(
            processorSeconds / elapsedSeconds / Math.Max(1, Environment.ProcessorCount) * 100.0,
            0,
            100);

        this.previousProcessorTime = processorTime;
        this.previousSampleUptime = nowUptime;

        var workingSetMb = this.process.WorkingSet64 / 1024.0 / 1024.0;
        var managedMemoryMb = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
        var snapshotCount = Interlocked.Read(ref this.totalSnapshots);
        var snapshotsPerSecond = (snapshotCount - this.snapshotsAtPreviousSample) / elapsedSeconds;
        this.snapshotsAtPreviousSample = snapshotCount;

        DiagnosticsContext context;
        try
        {
            context = this.contextProvider?.Invoke() ?? new DiagnosticsContext(false, "Aucun", "-", 0, "-", 0);
        }
        catch
        {
            context = new DiagnosticsContext(false, "Indisponible", "-", 0, "-", 0);
        }

        double averageCpu;
        lock (this.syncRoot)
        {
            this.cpuSum += cpuPercent;
            this.cpuSampleCount++;
            this.maximumCpu = Math.Max(this.maximumCpu, cpuPercent);
            this.maximumWorkingSetMb = Math.Max(this.maximumWorkingSetMb, workingSetMb);
            averageCpu = this.cpuSum / this.cpuSampleCount;
        }

        return new DiagnosticsSample(
            DateTimeOffset.Now,
            nowUptime,
            cpuPercent,
            averageCpu,
            this.maximumCpu,
            workingSetMb,
            this.maximumWorkingSetMb,
            managedMemoryMb,
            this.process.Threads.Count,
            this.process.HandleCount,
            snapshotsPerSecond,
            snapshotCount,
            context.Recording,
            context.PttSource,
            context.Simulator,
            context.Com1Mhz,
            context.Station,
            context.MicrophoneGainDb);
    }

    private void WriteHeader()
    {
        lock (this.syncRoot)
        {
            this.writer.WriteLine("# PHONIE DEV0.4.2.0 - LOG DIAGNOSTIC DE LÉGÈRETÉ");
            this.writer.WriteLine($"# Session locale : {DateTimeOffset.Now:O}");
            this.writer.WriteLine($"# Dossier portable : {AppPaths.BaseDirectory}");
            this.writer.WriteLine($"# OS : {Environment.OSVersion}");
            this.writer.WriteLine($"# Processeurs logiques : {Environment.ProcessorCount}");
            this.writer.WriteLine($"# Runtime : {Environment.Version}");
            this.writer.WriteLine("# Les lignes PERF sont séparées par des tabulations.");
            this.writer.WriteLine("# timestamp\ttype\tuptime_s\tcpu_pct\tcpu_avg_pct\tcpu_max_pct\tworking_set_mb\tworking_set_max_mb\tmanaged_mb\tthreads\thandles\tsnapshots_s\tsnapshots_total\trecording\tptt_source\tsimulator\tcom1_mhz\tstation\tmic_gain_db");
        }
    }

    private void WriteSample(DiagnosticsSample sample)
    {
        var culture = CultureInfo.InvariantCulture;
        lock (this.syncRoot)
        {
            this.writer.WriteLine(string.Join('\t',
                sample.Timestamp.ToString("O", culture),
                "PERF",
                sample.Uptime.TotalSeconds.ToString("F1", culture),
                sample.CpuPercent.ToString("F3", culture),
                sample.AverageCpuPercent.ToString("F3", culture),
                sample.MaximumCpuPercent.ToString("F3", culture),
                sample.WorkingSetMb.ToString("F1", culture),
                sample.MaximumWorkingSetMb.ToString("F1", culture),
                sample.ManagedMemoryMb.ToString("F1", culture),
                sample.ThreadCount.ToString(culture),
                sample.HandleCount.ToString(culture),
                sample.SnapshotsPerSecond.ToString("F2", culture),
                sample.TotalSnapshots.ToString(culture),
                sample.Recording ? "1" : "0",
                CleanField(sample.PttSource),
                CleanField(sample.Simulator),
                sample.Com1Mhz.ToString("F3", culture),
                CleanField(sample.Station),
                sample.MicrophoneGainDb.ToString(culture)));
        }
    }

    private void WriteSummary()
    {
        lock (this.syncRoot)
        {
            var averageCpu = this.cpuSampleCount > 0 ? this.cpuSum / this.cpuSampleCount : 0;
            this.writer.WriteLine("# --- RÉSUMÉ DE SESSION ---");
            this.writer.WriteLine($"# Durée : {this.uptime.Elapsed.TotalMinutes:F1} min");
            this.writer.WriteLine($"# CPU moyen PHONIE : {averageCpu:F3} %");
            this.writer.WriteLine($"# CPU maximum PHONIE : {this.maximumCpu:F3} %");
            this.writer.WriteLine($"# Mémoire maximum : {this.maximumWorkingSetMb:F1} Mo");
            this.writer.WriteLine($"# Snapshots SimConnect : {Interlocked.Read(ref this.totalSnapshots)}");
            this.writer.WriteLine($"# PTT conservés : {this.pttCount} - durée totale {this.totalPttSeconds:F1} s");
            this.writer.WriteLine($"# Fin : {DateTimeOffset.Now:O}");
        }
    }

    private static void RotateLogs(string directory, int filesToKeepBeforeCurrent)
    {
        try
        {
            var files = Directory.EnumerateFiles(directory, "PHONIE-DEV*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            foreach (var file in files.Skip(Math.Max(0, filesToKeepBeforeCurrent)))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // A locked old log is harmless and can be retried next launch.
                }
            }
        }
        catch
        {
            // Log rotation must never prevent PHONIE from starting.
        }
    }

    private static string CleanField(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.cancellation?.Cancel();

        if (this.worker is not null)
        {
            try
            {
                await this.worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        this.WriteSummary();
        lock (this.syncRoot)
        {
            this.writer.Dispose();
        }

        this.cancellation?.Dispose();
        this.process.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Phonie.Core;
using Phonie.Models;
using Phonie.Services;

namespace Phonie;

public partial class MainWindow : Window
{
    private readonly SimConnectService simConnectService = new();
    private readonly AudioService audioService = new();
    private readonly SettingsService settingsService = new();
    private readonly GlobalKeyboardHook keyboardHook = new();
    private readonly JoystickService joystickService = new();
    private readonly DiagnosticsService diagnosticsService = new();
    private SpeechRecognitionService speechRecognitionService;
    private readonly GpuBenchmarkService gpuBenchmarkService = new();
    private readonly AtisService atisService = new();
    private readonly GroundOperationsCoordinator groundOperationsCoordinator = new();
    private ControllerSpeechService controllerSpeechService;
    private readonly DispatcherTimer audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly DispatcherTimer acknowledgementTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly HashSet<string> activePttSources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> activePttSourceLabels = new(StringComparer.Ordinal);
    private AppSettings settings;
    private readonly SpeechRecognitionProfile startupSpeechProfile;
    private SpeechRecognitionProfile selectedSpeechProfile;
    private bool speechProfileRestartRequired;
    private ConnectionState currentConnectionState = ConnectionState.Waiting;
    private bool suppressSettingsEvents = true;
    private bool awaitingPttKey;
    private bool awaitingJoystickButton;
    private bool pttHeld;
    private bool closing;
    private bool audioReady;
    private bool pttReady;
    private bool keyboardPttReady;
    private bool joystickMappingAvailable;
    private string? lastRecordingPath;
    private string? lastRadioSignature;
    private string diagnosticsPttSource = "Aucun";
    private string diagnosticsSimulator = "-";
    private string diagnosticsStation = "-";
    private double diagnosticsCom1;
    private readonly ConcurrentDictionary<string, AirportFacilityReport> airportReports = new(StringComparer.OrdinalIgnoreCase);
    private AirportFacilityReport? latestGroundAirportReport;
    private AirportFacilityReport? latestRadioAirportReport;
    private string currentGeographicIcao = string.Empty;
    private string currentRadioIcao = string.Empty;
    private string currentRadioContextSource = "Station radio non résolue";
    private SimulatorSnapshot? latestSnapshot;
    private AtisInformation? currentAtis;
    private OperationalFrequency currentOperationalFrequency = new(
        0,
        "FRÉQUENCE NON IDENTIFIÉE",
        OperationalRadioKind.Unknown,
        false,
        "PHONIE reste silencieux tant que le service n'est pas déterminé.",
        "Initialisation");
    private CancellationTokenSource? transcriptionCancellation;
    private CancellationTokenSource? speechSynthesisCancellation;
    private string? lastAtisAudioSignature;
    private CancellationTokenSource? turboWarmupCancellation;
    private CancellationTokenSource? benchmarkCancellation;
    private Task? turboWarmupTask;
    private Task? benchmarkTask;
    private Task? controllerSpeechTask;
    private bool transcriptionInProgress;
    private bool benchmarkInProgress;
    private bool pseudoMaximized;
    private bool currentPttAcknowledgementOnly;
    private GroundMapSnapshot? latestGroundMap;
    private Rect normalBounds;

    public MainWindow()
    {
        this.settings = this.settingsService.Load();
        this.startupSpeechProfile = SpeechRecognitionProfiles.Parse(this.settings.SpeechRecognitionProfile);
        this.selectedSpeechProfile = this.startupSpeechProfile;
        this.speechRecognitionService = new SpeechRecognitionService(this.startupSpeechProfile);
        this.controllerSpeechService = new ControllerSpeechService(this.audioService);
        ThemeService.Apply(this.settings.Theme);

        this.InitializeComponent();
        this.suppressSettingsEvents = false;

        this.simConnectService.StatusChanged += this.SimConnectService_OnStatusChanged;
        this.simConnectService.SnapshotReceived += this.SimConnectService_OnSnapshotReceived;
        this.simConnectService.LogMessage += this.Service_OnLogMessage;
        this.simConnectService.AirportDataReceived += this.SimConnectService_OnAirportDataReceived;
        this.simConnectService.GroundTrafficReceived += this.SimConnectService_OnGroundTrafficReceived;
        this.simConnectService.AirportContextChanged += this.SimConnectService_OnAirportContextChanged;

        this.audioService.LogMessage += this.Service_OnLogMessage;
        this.audioService.RecordingStateChanged += this.AudioService_OnRecordingStateChanged;
        this.audioService.RecordingCompleted += this.AudioService_OnRecordingCompleted;
        this.groundOperationsCoordinator.LogMessage += this.Service_OnLogMessage;
        this.controllerSpeechService.LogMessage += this.Service_OnLogMessage;

        this.keyboardHook.KeyPressed += this.KeyboardHook_OnKeyPressed;
        this.keyboardHook.KeyReleased += this.KeyboardHook_OnKeyReleased;

        this.joystickService.ButtonChanged += this.JoystickService_OnButtonChanged;
        this.joystickService.DevicesChanged += this.JoystickService_OnDevicesChanged;
        this.joystickService.LogMessage += this.Service_OnLogMessage;

        this.diagnosticsService.SampleAvailable += this.DiagnosticsService_OnSampleAvailable;
        this.speechRecognitionService.StatusChanged += this.WhisperService_OnStatusChanged;
        this.audioMeterTimer.Tick += this.AudioMeterTimer_OnTick;
        this.acknowledgementTimer.Tick += this.AcknowledgementTimer_OnTick;

        this.Loaded += this.MainWindow_OnLoaded;
        this.Closing += this.MainWindow_OnClosing;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SelectThemeInUi();
        this.SelectGainInUi();
        this.SelectSpeechProfileInUi();
        this.RefreshAudioDevices();
        this.UpdatePttLabels();
        this.suppressSettingsEvents = true;
        this.AutoTranscribeCheckBox.IsChecked = this.settings.AutoTranscribePtt;
        this.suppressSettingsEvents = false;

        this.lastRecordingPath = File.Exists(this.audioService.LastRecordingPath)
            ? this.audioService.LastRecordingPath
            : null;
        this.PlayLastRecordingButton.IsEnabled = this.lastRecordingPath is not null;
        this.UpdateWhisperStatus(this.speechRecognitionService.GetSelectedStatus());
        this.DiagnosticsPathText.Text = System.IO.Path.Combine("logs", System.IO.Path.GetFileName(this.diagnosticsService.SessionFilePath));

        this.diagnosticsService.Start(() => new DiagnosticsContext(
            this.pttHeld,
            this.diagnosticsPttSource,
            this.diagnosticsSimulator,
            this.diagnosticsCom1,
            this.diagnosticsStation,
            this.settings.MicrophoneGainDb));

        try
        {
            this.keyboardHook.Start();
            this.keyboardPttReady = true;
            this.pttReady = true;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT clavier global prêt sur {KeyName(this.settings.PttVirtualKey)}.");
        }
        catch (Exception exception)
        {
            this.keyboardPttReady = false;
            this.pttReady = this.joystickMappingAvailable;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT clavier global indisponible : {exception.Message}");
        }

        this.joystickService.Start();
        this.RefreshFooterState();
        this.audioMeterTimer.Start();
        this.acknowledgementTimer.Start();
        this.simConnectService.Start();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Log portable : {this.diagnosticsService.SessionFilePath}");
        var startupDefinition = SpeechRecognitionProfiles.Get(this.startupSpeechProfile);
        var runtimeOrder = this.speechRecognitionService.StartupWhisperUsesVulkan ? "Vulkan puis CPU" : "CPU";
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Profil ASR au démarrage : {startupDefinition.ShortName} - runtime Whisper {runtimeOrder}.");
        this.ScheduleTurboWarmup("démarrage");
    }

    private async void MainWindow_OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        this.closing = true;
        e.Cancel = true;
        this.audioMeterTimer.Stop();
        this.acknowledgementTimer.Stop();
        this.keyboardHook.Dispose();
        await this.joystickService.DisposeAsync();
        this.transcriptionCancellation?.Cancel();
        this.speechSynthesisCancellation?.Cancel();
        this.turboWarmupCancellation?.Cancel();
        this.benchmarkCancellation?.Cancel();
        await AwaitQuietlyAsync(this.turboWarmupTask);
        await AwaitQuietlyAsync(this.benchmarkTask);
        await AwaitQuietlyAsync(this.controllerSpeechTask);
        this.transcriptionCancellation?.Dispose();
        this.speechSynthesisCancellation?.Dispose();
        this.turboWarmupCancellation?.Dispose();
        this.benchmarkCancellation?.Dispose();
        this.speechRecognitionService.Dispose();
        this.controllerSpeechService.Dispose();
        this.audioService.Dispose();
        await this.simConnectService.DisposeAsync();
        await this.diagnosticsService.DisposeAsync();
        this.Close();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source) is not null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            this.ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try
            {
                this.DragMove();
            }
            catch (InvalidOperationException)
            {
                // The button can be released while Windows starts the drag operation.
            }
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => this.ToggleMaximize();

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => this.Close();

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
        {
            this.WindowState = WindowState.Normal;
            this.ApplyWorkAreaMaximize();
        }

        this.MaximizeButton.Content = this.pseudoMaximized ? "\uE923" : "\uE922";
        this.MaximizeButton.ToolTip = this.pseudoMaximized ? "Restaurer" : "Agrandir";
    }

    private void ToggleMaximize()
    {
        if (this.pseudoMaximized)
        {
            this.pseudoMaximized = false;
            this.Left = this.normalBounds.Left;
            this.Top = this.normalBounds.Top;
            this.Width = this.normalBounds.Width;
            this.Height = this.normalBounds.Height;
        }
        else
        {
            this.ApplyWorkAreaMaximize();
        }

        this.MaximizeButton.Content = this.pseudoMaximized ? "\uE923" : "\uE922";
        this.MaximizeButton.ToolTip = this.pseudoMaximized ? "Restaurer" : "Agrandir";
    }

    private void ApplyWorkAreaMaximize()
    {
        if (!this.pseudoMaximized)
        {
            this.normalBounds = new Rect(this.Left, this.Top, this.ActualWidth, this.ActualHeight);
        }

        var workArea = this.GetCurrentMonitorWorkArea();
        this.pseudoMaximized = true;
        this.Left = workArea.Left;
        this.Top = workArea.Top;
        this.Width = workArea.Width;
        this.Height = workArea.Height;
    }

    private Rect GetCurrentMonitorWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return SystemParameters.WorkArea;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new Point(info.WorkArea.Left, info.WorkArea.Top));
        var bottomRight = transform.Transform(new Point(info.WorkArea.Right, info.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private async void ReconnectButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Reconnexion manuelle demandée.");
        await this.simConnectService.RequestReconnectAsync();
    }

    private void RefreshAudioButton_OnClick(object sender, RoutedEventArgs e) => this.RefreshAudioDevices();

    private void PlayLastRecordingButton_OnClick(object sender, RoutedEventArgs e)
    {
        var output = this.OutputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        if (!this.audioService.PlayFile(output?.Id, this.lastRecordingPath))
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Aucun enregistrement PTT lisible.");
        }
    }

    private void ChoosePttButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.awaitingJoystickButton = false;
        this.AssignJoystickPttButton.Content = "Assigner un bouton HOTAS";
        this.awaitingPttKey = true;
        this.ChoosePttButton.Content = "Appuyez…";
        this.PttStateText.Text = "Échap pour annuler";
        this.SetDotResource(this.PttFooterDot, "Warning");
    }

    private void AssignJoystickPttButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.awaitingJoystickButton)
        {
            this.CancelJoystickAssignment();
            return;
        }

        this.awaitingPttKey = false;
        this.ChoosePttButton.Content = "Clavier";
        this.awaitingJoystickButton = true;
        this.AssignJoystickPttButton.Content = "Annuler l'assignation";
        this.PttStateText.Text = "Appuyez sur un bouton HOTAS…";
        this.SetDotResource(this.PttFooterDot, "Warning");
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Assignation HOTAS : attente du prochain bouton.");
    }

    private void ClearJoystickPttButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.settings.JoystickPttDeviceKey = null;
        this.settings.JoystickPttDeviceName = null;
        this.settings.JoystickPttButton = null;
        this.joystickMappingAvailable = false;
        this.pttReady = this.keyboardPttReady;
        this.CancelJoystickAssignment();
        this.SaveSettings();
        this.UpdatePttLabels();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Affectation PTT HOTAS supprimée.");
    }

    private void MarkDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var markerName = (this.MarkerComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(markerName))
        {
            markerName = "TEST MANUEL";
        }

        var marker = $"{markerName} - {this.diagnosticsSimulator} - COM1 {this.diagnosticsCom1:F3} - {this.diagnosticsStation} - PTT {this.diagnosticsPttSource} - gain +{this.settings.MicrophoneGainDb} dB";
        this.diagnosticsService.Mark(marker);
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Marque ajoutée : {markerName}.");
    }

    private void RequestLfbiAirportDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        var icao = !string.IsNullOrWhiteSpace(this.currentGeographicIcao)
            ? this.currentGeographicIcao
            : !string.IsNullOrWhiteSpace(this.currentRadioIcao)
                ? this.currentRadioIcao
                : NormalizeIcao(this.settings.PreferredAirportIcao);
        if (string.IsNullOrWhiteSpace(icao))
        {
            this.AirportDataText.Text = "Facilities : aucun aérodrome résolu";
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Airport Data : aucun ICAO disponible à recharger.");
            return;
        }

        this.AirportDataText.Text = $"Facilities : rechargement {icao}...";
        if (!this.simConnectService.RequestAirportData(icao))
        {
            this.AirportDataText.Text = "Facilities : SimConnect indisponible";
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Airport Data {icao} : attendre la connexion au simulateur.");
        }
    }

    private void OpenDiagnosticsFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{this.diagnosticsService.DirectoryPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Ouverture du dossier impossible : {exception.Message}");
        }
    }


    private void ToggleDiagnosticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        this.DiagnosticOverlay.Visibility = this.DiagnosticOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void DownloadWhisperButton_OnClick(object sender, RoutedEventArgs e)
    {
        var definition = SpeechRecognitionProfiles.Get(this.selectedSpeechProfile);
        if (this.speechProfileRestartRequired)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Profil {definition.ShortName} enregistré. Redémarrage requis avant installation.");
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            return;
        }

        this.DownloadWhisperButton.IsEnabled = false;
        try
        {
            this.transcriptionCancellation?.Cancel();
            this.transcriptionCancellation?.Dispose();
            this.transcriptionCancellation = new CancellationTokenSource();
            await this.speechRecognitionService.DownloadSelectedModelAsync(this.transcriptionCancellation.Token);
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] {definition.DisplayName} téléchargé et vérifié.");
        }
        catch (OperationCanceledException)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Téléchargement ASR annulé.");
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Téléchargement ASR impossible : {CleanMessage(exception)}");
        }
        finally
        {
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.ScheduleTurboWarmup("installation du modèle");
        }
    }

    private void ScheduleTurboWarmup(string reason)
    {
        if (this.closing
            || this.benchmarkInProgress
            || this.selectedSpeechProfile != SpeechRecognitionProfile.WhisperLargeV3TurboVulkan
            || this.speechProfileRestartRequired
            || !this.speechRecognitionService.IsModelReady(SpeechRecognitionProfile.WhisperLargeV3TurboVulkan)
            || this.turboWarmupTask is { IsCompleted: false })
        {
            return;
        }

        this.turboWarmupCancellation?.Cancel();
        this.turboWarmupCancellation?.Dispose();
        var warmupCancellation = new CancellationTokenSource();
        this.turboWarmupCancellation = warmupCancellation;
        this.turboWarmupTask = this.RunTurboWarmupAsync(reason, warmupCancellation.Token);
    }

    private async Task RunTurboWarmupAsync(string reason, CancellationToken cancellationToken)
    {
        var completed = false;
        try
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Initialisation du moteur qualité Turbo Vulkan ({reason}).");
            var result = await Task.Run(
                () => this.speechRecognitionService.WarmUpTurboAsync(cancellationToken),
                cancellationToken);
            completed = true;
            this.diagnosticsService.WriteEvent(
                "ASR_WARMUP",
                $"Whisper Large-v3 Turbo Vulkan - chargement {result.ModelLoadTime.TotalSeconds:F2} s - total {result.EndToEndTime.TotalSeconds:F2} s");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Moteur qualité Turbo prêt en {result.EndToEndTime.TotalSeconds:F1} s.");
        }
        catch (OperationCanceledException)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Initialisation Turbo annulée.");
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Initialisation Turbo impossible : {CleanMessage(exception)}");
        }
        finally
        {
            if (!this.closing && !completed)
            {
                this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            }
        }
    }

    private async void GpuBenchmarkButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.benchmarkInProgress || this.transcriptionInProgress)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Une opération ASR est déjà en cours.");
            return;
        }

        var benchmarkAudioPath = this.lastRecordingPath;
        if (string.IsNullOrWhiteSpace(benchmarkAudioPath) || !File.Exists(benchmarkAudioPath))
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Enregistrez d'abord un PTT de référence pour le benchmark GPU.");
            return;
        }

        if (!this.speechRecognitionService.StartupWhisperUsesVulkan)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Benchmark GPU : redémarrez PHONIE avec un profil Whisper Vulkan.");
            return;
        }

        this.turboWarmupCancellation?.Cancel();
        await AwaitQuietlyAsync(this.turboWarmupTask);
        this.benchmarkInProgress = true;
        this.benchmarkCancellation?.Cancel();
        this.benchmarkCancellation?.Dispose();
        var benchmarkCancellation = new CancellationTokenSource();
        this.benchmarkCancellation = benchmarkCancellation;
        this.GpuBenchmarkButton.IsEnabled = false;
        this.CompareAsrButton.IsEnabled = false;
        this.TranscribeLastButton.IsEnabled = false;
        this.DownloadWhisperButton.IsEnabled = false;
        this.ExchangeLatencyText.Text = "BENCH GPU...";
        this.PilotTranscriptTextBox.Text = "Benchmark GPU/VRAM en cours : trois passages par moteur puis mesure de libération pendant 30 secondes.";

        var progress = new Progress<string>(message =>
        {
            this.WhisperStatusText.Text = message;
            this.PilotTranscriptTextBox.Text = message;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] BENCH GPU : {message}");
        });

        try
        {
            var benchmark = Task.Run(
                () => this.gpuBenchmarkService.RunAsync(
                    benchmarkAudioPath,
                    this.speechRecognitionService,
                    progress,
                    benchmarkCancellation.Token),
                benchmarkCancellation.Token);
            this.benchmarkTask = benchmark;
            var report = await benchmark;
            var successful = report.Runs.Where(run => run.Success).ToArray();
            var turboHot = successful
                .Where(run => run.Profile == SpeechRecognitionProfile.WhisperLargeV3TurboVulkan && !run.ColdStart)
                .OrderBy(run => run.EndToEndMilliseconds)
                .FirstOrDefault();
            var maxVram = successful.Length == 0 ? 0 : successful.Max(run => run.PeakDedicatedBytes);
            this.ExchangeLatencyText.Text = "BENCH TERMINÉ";
            this.PilotTranscriptTextBox.Text = string.Join(
                Environment.NewLine,
                "Benchmark terminé.",
                $"Backend : {report.BackendEvidence}",
                $"Turbo à chaud : {(turboHot is null ? "non mesuré" : $"{turboHot.EndToEndMilliseconds / 1000.0:F2} s")}.",
                $"VRAM PHONIE maximale observée : {FormatMemory(maxVram)}.",
                $"Rapports : logs\\benchmarks\\{System.IO.Path.GetFileName(report.TextPath)} et {System.IO.Path.GetFileName(report.JsonPath)}");
            this.diagnosticsService.WriteEvent(
                "GPU_BENCH",
                $"{report.BackendEvidence} - VRAM max {FormatMemory(maxVram)} - rapport {System.IO.Path.GetFileName(report.TextPath)}");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Benchmark GPU terminé : {report.TextPath}");
        }
        catch (OperationCanceledException)
        {
            this.ExchangeLatencyText.Text = "BENCH ANNULÉ";
            this.PilotTranscriptTextBox.Text = "Benchmark GPU annulé.";
        }
        catch (Exception exception)
        {
            this.ExchangeLatencyText.Text = "ERREUR BENCH";
            this.PilotTranscriptTextBox.Text = $"Benchmark GPU impossible : {CleanMessage(exception)}";
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Benchmark GPU impossible : {CleanMessage(exception)}");
        }
        finally
        {
            this.benchmarkInProgress = false;
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.ScheduleTurboWarmup("fin du benchmark");
        }
    }

    private async void TranscribeLastButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(this.lastRecordingPath) || !File.Exists(this.lastRecordingPath))
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Aucun enregistrement PTT disponible pour la reconnaissance.");
            return;
        }

        await this.TranscribeAndProcessAsync(this.lastRecordingPath, true);
    }

    private async void CompareAsrButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.transcriptionInProgress || this.benchmarkInProgress)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Une reconnaissance ou un benchmark est déjà en cours.");
            return;
        }

        if (string.IsNullOrWhiteSpace(this.lastRecordingPath) || !File.Exists(this.lastRecordingPath))
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Aucun enregistrement PTT disponible pour la comparaison.");
            return;
        }

        this.transcriptionInProgress = true;
        this.transcriptionCancellation?.Cancel();
        this.transcriptionCancellation?.Dispose();
        this.transcriptionCancellation = new CancellationTokenSource();
        this.ExchangeLatencyText.Text = "COMPARAISON...";
        this.PilotTranscriptTextBox.Text = "Comparaison des profils ASR installés sur le même enregistrement...";
        this.CompareAsrButton.IsEnabled = false;
        this.TranscribeLastButton.IsEnabled = false;
        try
        {
            var results = await this.speechRecognitionService.CompareInstalledAsync(
                this.lastRecordingPath,
                this.transcriptionCancellation.Token);
            var expectedCallsign = this.latestSnapshot?.AircraftAtcId;
            var lines = results.Select(result =>
            {
                var name = SpeechRecognitionProfiles.Get(result.Profile).ShortName;
                if (!result.WasRun)
                {
                    return $"{name} - {result.Status}";
                }

                var analysis = PhraseologyService.Analyze(result.Transcript, expectedCallsign);
                return $"{name} - {result.ProcessingTime.TotalSeconds:F2} s\n" +
                       $"Indicatif {analysis.Callsign ?? "-"} | Station {analysis.CalledStation ?? "-"} | Intention {analysis.Intention ?? "-"}\n" +
                       result.Transcript;
            });
            this.PilotTranscriptTextBox.Text = string.Join(Environment.NewLine + Environment.NewLine, lines);
            this.ExchangeLatencyText.Text = "COMPARAISON TERMINÉE";
            foreach (var result in results)
            {
                var operationalSummary = result.WasRun
                    ? FormatAnalysis(PhraseologyService.Analyze(result.Transcript, expectedCallsign))
                    : result.Status;
                this.diagnosticsService.WriteEvent(
                    "ASR_COMPARE",
                    $"{SpeechRecognitionProfiles.Get(result.Profile).ShortName} - {(result.WasRun ? $"{result.ProcessingTime.TotalSeconds:F2} s - {operationalSummary} - {result.Transcript}" : result.Status)}");
            }
        }
        catch (OperationCanceledException)
        {
            this.ExchangeLatencyText.Text = "ANNULÉ";
        }
        catch (Exception exception)
        {
            this.ExchangeLatencyText.Text = "ERREUR COMPARAISON";
            this.PilotTranscriptTextBox.Text = $"Comparaison impossible : {CleanMessage(exception)}";
        }
        finally
        {
            this.transcriptionInProgress = false;
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.ScheduleTurboWarmup("fin de la comparaison");
        }
    }

    private void AnalyzeLabTextButton_OnClick(object sender, RoutedEventArgs e)
    {
        var text = this.LabInputTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            this.PilotTranscriptTextBox.Text = "Saisissez une phrase dans le mode laboratoire.";
            return;
        }

        this.ProcessPilotText(text, false, TimeSpan.Zero);
    }

    private void AutoTranscribeCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (this.suppressSettingsEvents || !this.IsInitialized || this.AutoTranscribeCheckBox is null)
        {
            return;
        }

        this.settings.AutoTranscribePtt = this.AutoTranscribeCheckBox.IsChecked == true;
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Transcription automatique PTT : {(this.settings.AutoTranscribePtt ? "activée" : "désactivée")}.");
    }

    private void SpeechProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents
            || this.SpeechProfileComboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string tag
            || !Enum.TryParse<SpeechRecognitionProfile>(tag, true, out var profile))
        {
            return;
        }

        if (profile != SpeechRecognitionProfile.WhisperLargeV3TurboVulkan)
        {
            this.turboWarmupCancellation?.Cancel();
        }

        this.selectedSpeechProfile = profile;
        this.settings.SpeechRecognitionProfile = profile.ToString();
        this.speechProfileRestartRequired = !this.speechRecognitionService.SelectProfile(profile);
        this.SaveSettings();
        var definition = SpeechRecognitionProfiles.Get(profile);
        if (this.speechProfileRestartRequired)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Profil {definition.ShortName} enregistré. Redémarrez PHONIE pour changer de runtime Whisper.");
        }
        else
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Profil ASR actif : {definition.ShortName}.");
        }

        this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(profile));
        this.ScheduleTurboWarmup("sélection du profil");
    }

    private void ClearLogButton_OnClick(object sender, RoutedEventArgs e) => this.LogBox.Clear();

    private void ThemeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string theme)
        {
            return;
        }

        this.settings.Theme = theme;
        ThemeService.Apply(theme);
        this.RefreshVisualStates();
        this.SaveSettings();
    }

    private void InputDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.InputDeviceComboBox.SelectedItem is not AudioDeviceInfo selectedDevice)
        {
            return;
        }

        this.settings.InputDeviceId = selectedDevice.Id;
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Microphone : {selectedDevice.Name}");
    }

    private void OutputDeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents || this.OutputDeviceComboBox.SelectedItem is not AudioDeviceInfo selectedDevice)
        {
            return;
        }

        this.settings.OutputDeviceId = selectedDevice.Id;
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Sortie radio : {selectedDevice.Name}");
    }

    private void GainComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (this.suppressSettingsEvents
            || this.GainComboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is not string gainText
            || !int.TryParse(gainText, out var gainDb))
        {
            return;
        }

        this.settings.MicrophoneGainDb = Math.Clamp(gainDb, 0, 18);
        this.SaveSettings();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Gain micro PHONIE : +{this.settings.MicrophoneGainDb} dB.");
    }

    private void RefreshAudioDevices()
    {
        this.suppressSettingsEvents = true;
        try
        {
            var inputs = this.audioService.GetInputDevices();
            var outputs = this.audioService.GetOutputDevices();

            this.InputDeviceComboBox.ItemsSource = inputs;
            this.OutputDeviceComboBox.ItemsSource = outputs;

            var inputId = DeviceExists(inputs, this.settings.InputDeviceId)
                ? this.settings.InputDeviceId
                : this.audioService.GetDefaultInputDeviceId();
            var outputId = DeviceExists(outputs, this.settings.OutputDeviceId)
                ? this.settings.OutputDeviceId
                : this.audioService.GetDefaultOutputDeviceId();

            this.InputDeviceComboBox.SelectedItem = inputs.FirstOrDefault(device => device.Id == inputId) ?? inputs.FirstOrDefault();
            this.OutputDeviceComboBox.SelectedItem = outputs.FirstOrDefault(device => device.Id == outputId) ?? outputs.FirstOrDefault();

            this.settings.InputDeviceId = (this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
            this.settings.OutputDeviceId = (this.OutputDeviceComboBox.SelectedItem as AudioDeviceInfo)?.Id;
            this.SaveSettings();

            this.audioReady = inputs.Count > 0 && outputs.Count > 0;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Audio prêt : {inputs.Count} entrée(s), {outputs.Count} sortie(s).");
        }
        catch (Exception exception)
        {
            this.audioReady = false;
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Initialisation audio impossible : {exception.Message}");
        }
        finally
        {
            this.suppressSettingsEvents = false;
            this.RefreshFooterState();
        }
    }

    private void KeyboardHook_OnKeyPressed(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (this.awaitingJoystickButton)
            {
                if (virtualKey == 0x1B)
                {
                    this.CancelJoystickAssignment();
                }

                return;
            }

            if (this.awaitingPttKey)
            {
                if (virtualKey == 0x1B)
                {
                    this.awaitingPttKey = false;
                    this.ChoosePttButton.Content = "Clavier";
                    this.PttStateText.Text = "Relâché";
                    this.RefreshFooterState();
                    return;
                }

                this.settings.PttVirtualKey = virtualKey;
                this.awaitingPttKey = false;
                this.ChoosePttButton.Content = "Clavier";
                this.UpdatePttLabels();
                this.PttStateText.Text = "Relâché";
                this.SaveSettings();
                this.RefreshFooterState();
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Touche PTT définie : {KeyName(virtualKey)}.");
                return;
            }

            if (virtualKey != this.settings.PttVirtualKey)
            {
                return;
            }

            this.BeginPtt($"KEY:{virtualKey}", $"Clavier {KeyName(virtualKey)}");
        });
    }

    private void KeyboardHook_OnKeyReleased(object? sender, int virtualKey)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (virtualKey == this.settings.PttVirtualKey)
            {
                this.EndPtt($"KEY:{virtualKey}");
            }
        });
    }

    private void JoystickService_OnButtonChanged(object? sender, JoystickButtonEvent buttonEvent)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            if (this.awaitingJoystickButton && buttonEvent.IsPressed)
            {
                this.settings.JoystickPttDeviceKey = buttonEvent.Device.Key;
                this.settings.JoystickPttDeviceName = buttonEvent.Device.Name;
                this.settings.JoystickPttButton = buttonEvent.ButtonNumber;
                this.joystickMappingAvailable = true;
                this.pttReady = true;
                this.CancelJoystickAssignment();
                this.SaveSettings();
                this.UpdatePttLabels();
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT HOTAS défini : {buttonEvent.Device.Name} - bouton {buttonEvent.ButtonNumber}.");
                return;
            }

            if (!this.MatchesConfiguredJoystickButton(buttonEvent))
            {
                return;
            }

            var sourceKey = $"JOY:{buttonEvent.Device.Key}:{buttonEvent.ButtonNumber}";
            var sourceLabel = $"HOTAS {buttonEvent.Device.Name} B{buttonEvent.ButtonNumber}";
            if (buttonEvent.IsPressed)
            {
                this.BeginPtt(sourceKey, sourceLabel);
            }
            else
            {
                this.EndPtt(sourceKey);
            }
        });
    }

    private void JoystickService_OnDevicesChanged(object? sender, IReadOnlyList<JoystickDeviceInfo> devices)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.joystickMappingAvailable = !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey)
                && devices.Any(device => string.Equals(device.Key, this.settings.JoystickPttDeviceKey, StringComparison.Ordinal));

            if (!this.joystickMappingAvailable && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey))
            {
                foreach (var source in this.activePttSources
                             .Where(source => source.StartsWith($"JOY:{this.settings.JoystickPttDeviceKey}:", StringComparison.Ordinal))
                             .ToArray())
                {
                    this.EndPtt(source);
                }
            }

            this.pttReady = this.keyboardPttReady || this.joystickMappingAvailable;
            this.UpdatePttLabels();
            this.RefreshFooterState();
        });
    }

    private bool MatchesConfiguredJoystickButton(JoystickButtonEvent buttonEvent) =>
        this.settings.JoystickPttButton == buttonEvent.ButtonNumber
        && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceKey)
        && string.Equals(this.settings.JoystickPttDeviceKey, buttonEvent.Device.Key, StringComparison.Ordinal);

    private void BeginPtt(string sourceKey, string sourceLabel)
    {
        if (!this.activePttSources.Add(sourceKey))
        {
            return;
        }

        this.activePttSourceLabels[sourceKey] = sourceLabel;
        this.UpdateActivePttSourceLabel();

        if (this.pttHeld)
        {
            return;
        }

        this.pttHeld = true;
        this.currentPttAcknowledgementOnly = this.groundOperationsCoordinator.AcknowledgePilotPtt();
        if (this.currentPttAcknowledgementOnly)
        {
            this.diagnosticsService.WriteEvent("RADIO", "Collationnement/accusé de réception détecté au PTT.");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Collationnement reçu au PTT - contenu non bloquant.");
            this.UpdateGroundOperationsUi();
        }

        var input = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        if (this.audioService.StartRecording(input?.Id, this.settings.MicrophoneGainDb))
        {
            this.diagnosticsService.WriteEvent("PTT", $"Début - {sourceLabel}");
            return;
        }

        this.activePttSources.Clear();
        this.activePttSourceLabels.Clear();
        this.pttHeld = false;
        this.currentPttAcknowledgementOnly = false;
        this.diagnosticsPttSource = "Aucun";
        this.PttStateText.Text = "Micro indisponible";
        this.SetDotResource(this.PttFooterDot, "Danger");
    }

    private void EndPtt(string sourceKey)
    {
        if (!this.activePttSources.Remove(sourceKey))
        {
            return;
        }

        this.activePttSourceLabels.Remove(sourceKey);
        this.UpdateActivePttSourceLabel();
        if (this.activePttSources.Count > 0 || !this.pttHeld)
        {
            return;
        }

        this.pttHeld = false;
        this.audioService.StopRecording();
    }

    private void UpdateActivePttSourceLabel()
    {
        this.diagnosticsPttSource = this.activePttSourceLabels.Count == 0
            ? "Aucun"
            : string.Join(" + ", this.activePttSourceLabels.Values);
    }

    private void CancelJoystickAssignment()
    {
        this.awaitingJoystickButton = false;
        this.AssignJoystickPttButton.Content = "Assigner un bouton HOTAS";
        this.PttStateText.Text = "Relâché";
        this.RefreshFooterState();
    }

    private void AudioService_OnRecordingStateChanged(object? sender, bool recording)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.SetBackgroundResource(this.PttPanel, recording ? "PttActiveBackground" : "PttIdleBackground");
            this.PttStateText.Text = recording ? "ÉMISSION - parlez" : "Relâché";
            this.SetForegroundResource(this.PttStateText, recording ? "Success" : "SecondaryText");
            this.SetDotResource(this.PttFooterDot, recording ? "Accent" : this.pttReady ? "Success" : "Danger");
        });
    }

    private void AudioService_OnRecordingCompleted(object? sender, AudioRecordingResult result)
    {
        _ = this.Dispatcher.BeginInvoke(async () =>
        {
            if (result.WasDiscarded)
            {
                this.PttStateText.Text = "Appui trop court - ignoré";
                this.SetForegroundResource(this.PttStateText, "Warning");
                this.SetDotResource(this.PttFooterDot, this.pttReady ? "Success" : "Warning");
                this.diagnosticsService.WriteEvent("PTT", $"Ignoré - {result.Duration.TotalSeconds:F2} s - seuil 0,25 s");
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] PTT ignoré : appui de {result.Duration.TotalSeconds:F2} s.");
                this.currentPttAcknowledgementOnly = false;
                return;
            }

            this.lastRecordingPath = result.FilePath;
            this.PlayLastRecordingButton.IsEnabled = result.FileSizeBytes > 44;
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.PttStateText.Text = $"Enregistré - +{result.GainDb} dB";
            this.SetForegroundResource(this.PttStateText, result.LimitedSampleCount > 0 ? "Warning" : "Success");
            this.SetDotResource(this.PttFooterDot, "Success");
            this.diagnosticsService.ReportPttCompleted(result.Duration);
            this.diagnosticsService.WriteEvent(
                "PTT",
                $"Fin - {result.Duration.TotalSeconds:F1} s - {result.FileSizeBytes / 1024.0:F0} Ko - gain +{result.GainDb} dB - limiteur {result.LimitedSampleCount} - pic {result.PeakPercent:F0} %");
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] PTT enregistré : {result.Duration.TotalSeconds:F1} s - +{result.GainDb} dB - pic {result.PeakPercent:F0} % - limiteur {result.LimitedSampleCount}.");

            if (this.settings.AutoTranscribePtt)
            {
                if (this.speechProfileRestartRequired)
                {
                    this.PilotTranscriptTextBox.Text = "PTT enregistré. Redémarrez PHONIE pour activer le profil ASR sélectionné.";
                    this.ExchangeLatencyText.Text = "REDÉMARRAGE REQUIS";
                    this.currentPttAcknowledgementOnly = false;
                }
                else if (this.speechRecognitionService.IsSelectedModelReady)
                {
                    await this.TranscribeAndProcessAsync(
                        result.FilePath,
                        true,
                        this.currentPttAcknowledgementOnly);
                    this.currentPttAcknowledgementOnly = false;
                }
                else
                {
                    this.PilotTranscriptTextBox.Text = "PTT enregistré. Installez le modèle du profil ASR sélectionné.";
                    this.ExchangeLatencyText.Text = "MODÈLE MANQUANT";
                    this.currentPttAcknowledgementOnly = false;
                }
            }
            else
            {
                this.currentPttAcknowledgementOnly = false;
            }
        });
    }

    private void AcknowledgementTimer_OnTick(object? sender, EventArgs e)
    {
        if (this.pttHeld || this.closing)
        {
            return;
        }

        var reminder = this.groundOperationsCoordinator.PollAcknowledgement(DateTimeOffset.Now);
        if (reminder is null || string.IsNullOrWhiteSpace(reminder.SpokenText))
        {
            return;
        }

        this.ControllerResponseTextBox.Text = $"PHONIE : {reminder.SpokenText}";
        this.diagnosticsService.WriteEvent("RADIO", $"Relance collationnement : {reminder.SpokenText}");
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Relance collationnement : {reminder.SpokenText}");
        this.UpdateGroundOperationsUi();
        this.StartControllerSpeech(reminder.SpokenText, reminder.RequiresAcknowledgement);
    }

    private void AudioMeterTimer_OnTick(object? sender, EventArgs e)
    {
        var selectedInput = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        var rawPeak = Math.Clamp(this.audioService.GetInputPeak(selectedInput?.Id), 0.0f, 1.0f);
        var gainLinear = Math.Pow(10.0, this.settings.MicrophoneGainDb / 20.0);
        var amplifiedPeak = rawPeak * gainLinear;
        var level = Math.Clamp(amplifiedPeak * 100.0, 0.0, 100.0);
        this.MicrophoneLevelBar.Value = level;

        var meterResource = amplifiedPeak >= 1.0 ? "MeterHigh" : amplifiedPeak >= 0.75 ? "MeterMedium" : "MeterLow";
        this.MicrophoneLevelBar.SetResourceReference(ProgressBar.ForegroundProperty, meterResource);

        if (amplifiedPeak >= 1.0)
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - saturation, réduire le gain";
            this.SetForegroundResource(this.MicrophoneSaturationText, "Danger");
        }
        else if (amplifiedPeak >= 0.75)
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - niveau élevé";
            this.SetForegroundResource(this.MicrophoneSaturationText, "Warning");
        }
        else
        {
            this.MicrophoneSaturationText.Text = $"Gain PHONIE : +{this.settings.MicrophoneGainDb} dB - niveau {level:F0} %";
            this.SetForegroundResource(this.MicrophoneSaturationText, "SecondaryText");
        }
    }

    private void SimConnectService_OnStatusChanged(object? sender, ConnectionStatus status)
    {
        if (status.State is ConnectionState.Disconnected or ConnectionState.Waiting)
        {
            this.airportReports.Clear();
            this.latestGroundAirportReport = null;
            this.latestRadioAirportReport = null;
            this.currentGeographicIcao = string.Empty;
            this.currentRadioIcao = string.Empty;
            this.currentRadioContextSource = "Station radio non résolue";
            this.latestSnapshot = null;
            this.currentAtis = null;
            this.lastAtisAudioSignature = null;
            this.lastRadioSignature = null;
            this.atisService.Reset();
            this.groundOperationsCoordinator.ClearAirport("connexion SimConnect inactive");
        }

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.currentConnectionState = status.State;
            this.ConnectionText.Text = status.Message;
            var resource = status.State switch
            {
                ConnectionState.Connected => "Success",
                ConnectionState.Error => "Danger",
                ConnectionState.Disconnected => "Danger",
                ConnectionState.Connecting => "Warning",
                _ => "Warning",
            };

            this.SetDotResource(this.StatusDot, resource);
            this.SetDotResource(this.SimFooterDot, resource);
            if (status.State is ConnectionState.Disconnected or ConnectionState.Waiting)
            {
                this.AirportNameText.Text = "Aérodrome - en attente de SimConnect";
                this.AirportSummaryText.Text = "Contexte géographique réinitialisé.";
                this.FrequencySummaryText.Text = "Fréquences : en attente de SimConnect.";
                this.AirportDataText.Text = "Contexte : aucun aérodrome actif";
                this.UpdateGroundOperationsUi();
                this.UpdateAtisUi();
            }

            this.RefreshFooterState();
        });
    }

    private void SimConnectService_OnSnapshotReceived(object? sender, SimulatorSnapshot snapshot)
    {
        this.diagnosticsService.ReportSnapshot();
        this.latestSnapshot = snapshot;
        this.ApplyAirportContexts(
            snapshot.GeographicAirportIcao,
            snapshot.GeographicAirportDistanceNm,
            snapshot.RadioAirportIcao,
            snapshot.RadioContextSource);

        this.currentOperationalFrequency = OperationalRadioService.Resolve(
            snapshot,
            this.latestRadioAirportReport,
            this.currentRadioIcao);
        this.groundOperationsCoordinator.UpdateSnapshot(snapshot);
        var runwayForAtis = string.Equals(
                this.currentRadioIcao,
                this.currentGeographicIcao,
                StringComparison.OrdinalIgnoreCase)
            ? this.groundOperationsCoordinator.CurrentRunwayDesignator
            : null;
        this.currentAtis = this.atisService.Update(
            snapshot,
            this.latestRadioAirportReport,
            runwayForAtis);

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.diagnosticsSimulator = snapshot.Simulator;
            this.diagnosticsCom1 = snapshot.Com1ActiveMhz;
            this.diagnosticsStation = this.currentOperationalFrequency.ServiceName;

            this.SimulatorText.Text = snapshot.Simulator;
            var aircraftTitle = string.IsNullOrWhiteSpace(snapshot.AircraftTitle)
                ? "Avion sans titre SimConnect"
                : snapshot.AircraftTitle;
            this.AircraftText.Text = string.IsNullOrWhiteSpace(snapshot.AircraftAtcId)
                ? aircraftTitle
                : $"{aircraftTitle} - ATC ID {snapshot.AircraftAtcId}";

            this.PositionText.Text = FormatCoordinates(snapshot.Latitude, snapshot.Longitude);
            this.DistanceText.Text = string.IsNullOrWhiteSpace(snapshot.GeographicAirportIcao)
                ? "Aérodrome géographique : recherche..."
                : $"Aérodrome {snapshot.GeographicAirportIcao} : {snapshot.GeographicAirportDistanceNm:F1} NM";
            this.GroundText.Text = snapshot.IsOnGround ? "AU SOL" : "EN VOL";
            this.AltitudeText.Text = $"Altitude : {snapshot.AltitudeFeet:F0} ft";
            this.HeadingText.Text = $"Cap : {snapshot.HeadingMagneticDegrees:000}° M";
            this.SpeedText.Text = $"IAS {snapshot.IndicatedAirspeedKnots:F0} kt - GS {snapshot.GroundSpeedKnots:F0} kt";
            this.ComActiveText.Text = $"Active : {snapshot.Com1ActiveMhz:F3}";
            this.ComStandbyText.Text = $"Standby : {snapshot.Com1StandbyMhz:F3}";
            this.XpdrText.Text = snapshot.TransponderCode;
            this.LastUpdateText.Text = $"Actualisé à {snapshot.Timestamp:HH:mm:ss}";

            var stationIdent = string.IsNullOrWhiteSpace(snapshot.Com1StationIdent) ? "Station non identifiée" : snapshot.Com1StationIdent;
            var stationType = FriendlyStationType(snapshot.Com1StationType);
            var spacing = snapshot.Com1SpacingMode == 1 ? "8,33 kHz" : "25 kHz";
            var receive = snapshot.Com1Receiving ? "réception active" : "réception coupée";
            var radioStatus = snapshot.Com1Status == 0 ? string.Empty : $" - statut {snapshot.Com1Status}";
            var radioAirport = string.IsNullOrWhiteSpace(snapshot.RadioAirportIcao)
                ? "contexte radio non résolu"
                : $"radio {snapshot.RadioAirportIcao}";
            this.ComMetaText.Text =
                $"{stationIdent} - {stationType} - {spacing} - {receive}{radioStatus} - {radioAirport} ({snapshot.RadioContextSource})";
            this.UpdateOperationalRadioUi();

            this.WeatherPrimaryText.Text = $"Vent {FormatDirection(snapshot.WindDirectionTrueDegrees)} / {FormatWindSpeed(snapshot.WindVelocityKnots)} - QNH {FormatQnh(snapshot.QnhHpa)}";
            var dewPoint = double.IsFinite(snapshot.DewPointCelsius)
                ? $" - rosée {snapshot.DewPointCelsius:F0} °C"
                : string.Empty;
            var ceiling = double.IsFinite(snapshot.CeilingFeet) && snapshot.CeilingFeet > 0
                ? $" - plafond {snapshot.CeilingFeet:F0} ft"
                : string.Empty;
            this.WeatherSecondaryText.Text =
                $"Température {FormatTemperature(snapshot.TemperatureCelsius)}{dewPoint} - " +
                $"visibilité {FormatVisibility(snapshot.VisibilityMeters)}{ceiling}";
            this.UpdateAtisUi();
            this.UpdateGroundOperationsUi();
            this.EnsureAtisCacheWhenNeeded();

            var radioSignature = $"{snapshot.Com1ActiveMhz:F3}|{this.currentOperationalFrequency.ServiceName}|{this.currentOperationalFrequency.Kind}|{snapshot.Com1SpacingMode}|{snapshot.Com1Receiving}|{snapshot.RadioAirportIcao}";
            if (!string.Equals(radioSignature, this.lastRadioSignature, StringComparison.Ordinal))
            {
                this.lastRadioSignature = radioSignature;
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] COM1 : {snapshot.Com1ActiveMhz:F3} - {this.currentOperationalFrequency.ServiceName} - {this.currentOperationalFrequency.Guidance}");
            }
        });
    }

    private void SimConnectService_OnAirportContextChanged(object? sender, AirportContextChanged context)
    {
        this.ApplyAirportContexts(
            context.GeographicIcao,
            context.GeographicDistanceNm,
            context.RadioIcao,
            context.RadioSource);

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            var geographic = string.IsNullOrWhiteSpace(context.GeographicIcao) ? "aucun" : context.GeographicIcao;
            var radio = string.IsNullOrWhiteSpace(context.RadioIcao) ? "non résolu" : context.RadioIcao;
            this.AirportDataText.Text =
                $"Contexte : sol {geographic} - radio {radio} - {context.NearbyAirports.Count} aérodrome(s) Facilities";

            if (this.latestGroundAirportReport is not null)
            {
                this.UpdateAirportUi(this.latestGroundAirportReport);
            }
            else if (!string.IsNullOrWhiteSpace(context.GeographicIcao))
            {
                this.AirportNameText.Text = $"{context.GeographicIcao} - chargement Facilities";
                this.AirportSummaryText.Text = "Pistes, parkings, taxiways et points d'attente en cours de rechargement.";
                this.FrequencySummaryText.Text = "Fréquences : rechargement du nouvel aérodrome.";
            }
            else
            {
                this.AirportNameText.Text = "Aérodrome - recherche en cours";
                this.AirportSummaryText.Text = "Aucun contexte géographique confirmé.";
                this.FrequencySummaryText.Text = "Fréquences : en attente d'un aérodrome ou d'une station radio.";
            }

            this.UpdateGroundOperationsUi();
            this.UpdateOperationalRadioUi();
            this.UpdateAtisUi();
        });
    }

    private void ApplyAirportContexts(
        string? geographicIcao,
        double geographicDistanceNm,
        string? radioIcao,
        string? radioSource)
    {
        var geographic = NormalizeIcao(geographicIcao);
        var radio = NormalizeIcao(radioIcao);
        var geographicChanged = !string.Equals(
            geographic,
            this.currentGeographicIcao,
            StringComparison.OrdinalIgnoreCase);
        var radioChanged = !string.Equals(
            radio,
            this.currentRadioIcao,
            StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                radioSource,
                this.currentRadioContextSource,
                StringComparison.Ordinal);

        if (geographicChanged)
        {
            var previous = string.IsNullOrWhiteSpace(this.currentGeographicIcao) ? "aucun" : this.currentGeographicIcao;
            this.currentGeographicIcao = geographic;
            this.latestGroundAirportReport = null;
            this.groundOperationsCoordinator.ClearAirport(
                $"changement géographique {previous} -> {(string.IsNullOrWhiteSpace(geographic) ? "aucun" : geographic)}");
        }

        if (!string.IsNullOrWhiteSpace(geographic)
            && this.latestGroundAirportReport is null
            && this.TryGetAirportReport(geographic, out var groundReport))
        {
            this.latestGroundAirportReport = groundReport;
            this.groundOperationsCoordinator.UpdateAirport(groundReport);
        }

        if (radioChanged)
        {
            this.currentRadioIcao = radio;
            this.currentRadioContextSource = string.IsNullOrWhiteSpace(radioSource)
                ? "Station radio non résolue"
                : radioSource.Trim();
            this.latestRadioAirportReport = null;
            this.currentAtis = null;
            this.lastAtisAudioSignature = null;
            this.atisService.Reset();
        }

        if (!string.IsNullOrWhiteSpace(radio)
            && this.latestRadioAirportReport is null
            && this.TryGetAirportReport(radio, out var radioReport))
        {
            this.latestRadioAirportReport = radioReport;
        }

        _ = geographicDistanceNm; // Conservé dans le snapshot et affiché par l'interface.
    }

    private bool TryGetAirportReport(string icao, out AirportFacilityReport report)
    {
        if (this.airportReports.TryGetValue(icao, out var cached))
        {
            report = cached;
            return true;
        }

        if (this.simConnectService.TryGetAirportReport(icao, out var serviceReport)
            && serviceReport is not null)
        {
            this.airportReports[icao] = serviceReport;
            report = serviceReport;
            return true;
        }

        report = null!;
        return false;
    }

    private void SimConnectService_OnAirportDataReceived(object? sender, AirportFacilityReport report)
    {
        var icao = NormalizeIcao(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao);
        if (!string.IsNullOrWhiteSpace(icao))
        {
            this.airportReports[icao] = report;
        }

        var isGroundContext = !string.IsNullOrWhiteSpace(icao)
            && string.Equals(icao, this.currentGeographicIcao, StringComparison.OrdinalIgnoreCase);
        var isRadioContext = !string.IsNullOrWhiteSpace(icao)
            && string.Equals(icao, this.currentRadioIcao, StringComparison.OrdinalIgnoreCase);

        if (isGroundContext)
        {
            this.latestGroundAirportReport = report;
            this.groundOperationsCoordinator.UpdateAirport(report);
            if (this.latestSnapshot is not null)
            {
                this.groundOperationsCoordinator.UpdateSnapshot(this.latestSnapshot);
            }
        }

        if (isRadioContext)
        {
            this.latestRadioAirportReport = report;
            if (this.latestSnapshot is not null)
            {
                this.currentOperationalFrequency = OperationalRadioService.Resolve(
                    this.latestSnapshot,
                    report,
                    this.currentRadioIcao);
                var runwayForAtis = isGroundContext
                    ? this.groundOperationsCoordinator.CurrentRunwayDesignator
                    : null;
                this.currentAtis = this.atisService.Update(
                    this.latestSnapshot,
                    report,
                    runwayForAtis);
            }
        }

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            var roles = isGroundContext && isRadioContext
                ? "sol + radio"
                : isGroundContext
                    ? "sol"
                    : isRadioContext
                        ? "radio"
                        : "cache";
            this.AirportDataText.Text = $"{icao} ({roles}) : {report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s)";
            if (isGroundContext || isRadioContext)
            {
                this.UpdateAirportUi(report);
            }

            this.UpdateOperationalRadioUi();
            this.UpdateAtisUi();
            this.UpdateGroundOperationsUi();
            this.EnsureAtisCacheWhenNeeded();
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] Airport Data {icao} terminé ({roles}) : " +
                $"{report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s), " +
                $"{report.TaxiParkings.Count} parking(s), {report.TaxiPaths.Count} chemin(s), " +
                $"{report.ParseWarnings.Count} avertissement(s). " +
                $"TaxiPath : {report.DiagnosticSummary.ParsedTaxiPathPacketCount}/{report.DiagnosticSummary.TaxiPathPacketCount}. " +
                $"Capture : logs\\airport-data\\raw\\{System.IO.Path.GetFileName(report.DiagnosticDirectoryPath)}");
        });
    }

    private void SimConnectService_OnGroundTrafficReceived(object? sender, GroundTrafficSnapshot snapshot)
    {
        this.groundOperationsCoordinator.UpdateTraffic(
            snapshot,
            this.latestSnapshot?.AircraftAtcId ?? string.Empty);

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.UpdateGroundOperationsUi();
            if (!snapshot.ProviderAvailable)
            {
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Trafic sol : {snapshot.Status}");
            }
        });
    }

    private void WhisperService_OnStatusChanged(object? sender, SpeechModelStatus status) =>
        _ = this.Dispatcher.BeginInvoke(() => this.UpdateWhisperStatus(status));

    private void UpdateWhisperStatus(SpeechModelStatus status)
    {
        var definition = SpeechRecognitionProfiles.Get(this.selectedSpeechProfile);
        this.WhisperStatusText.Text = status.Message;
        this.WhisperProgressBar.Value = Math.Clamp(status.ProgressPercent, 0, 100);
        this.WhisperProgressBar.IsIndeterminate = status.State == SpeechModelState.WarmingUp;
        this.WhisperProgressBar.Visibility = status.State is SpeechModelState.Downloading or SpeechModelState.WarmingUp
            ? Visibility.Visible
            : Visibility.Collapsed;
        var busy = this.benchmarkInProgress
            || status.State is SpeechModelState.Downloading
                or SpeechModelState.Loading
                or SpeechModelState.WarmingUp
                or SpeechModelState.Transcribing;
        var modelReady = this.speechRecognitionService.IsModelReady(this.selectedSpeechProfile);
        this.SpeechProfileComboBox.IsEnabled = !busy && !this.transcriptionInProgress;
        this.DownloadWhisperButton.IsEnabled = !busy && !this.speechProfileRestartRequired;
        this.DownloadWhisperButton.Content = this.speechProfileRestartRequired
            ? "Redémarrer"
            : modelReady
                ? "Installé"
                : "Installer";
        this.TranscribeLastButton.IsEnabled = modelReady
            && !this.speechProfileRestartRequired
            && !busy
            && !this.transcriptionInProgress
            && !string.IsNullOrWhiteSpace(this.lastRecordingPath)
            && File.Exists(this.lastRecordingPath);
        this.CompareAsrButton.IsEnabled = !busy
            && !this.transcriptionInProgress
            && !string.IsNullOrWhiteSpace(this.lastRecordingPath)
            && File.Exists(this.lastRecordingPath);
        this.GpuBenchmarkButton.IsEnabled = this.speechRecognitionService.StartupWhisperUsesVulkan
            && !busy
            && !this.transcriptionInProgress
            && !string.IsNullOrWhiteSpace(this.lastRecordingPath)
            && File.Exists(this.lastRecordingPath);

        var resource = status.State switch
        {
            SpeechModelState.Ready => "Success",
            SpeechModelState.Downloading or SpeechModelState.Loading or SpeechModelState.WarmingUp or SpeechModelState.Transcribing => "Accent",
            SpeechModelState.RestartRequired => "Warning",
            SpeechModelState.Error => "Danger",
            _ => "Warning",
        };
        this.SetForegroundResource(this.WhisperStatusText, resource);
        this.DownloadWhisperButton.ToolTip = definition.Description;
        this.GpuBenchmarkButton.ToolTip = "Mesure Small Vulkan, Turbo Vulkan et Vosk sur le dernier WAV : GPU, VRAM, RAM, CPU, froid et chaud.";
    }

    private async Task TranscribeAndProcessAsync(
        string audioPath,
        bool fromMicrophone,
        bool acknowledgementOnly = false)
    {
        if (this.transcriptionInProgress || this.benchmarkInProgress)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Une reconnaissance ou un benchmark est déjà en cours.");
            return;
        }

        if (this.speechProfileRestartRequired)
        {
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.PilotTranscriptTextBox.Text = "Redémarrez PHONIE pour activer le runtime du profil sélectionné.";
            return;
        }

        if (!this.speechRecognitionService.IsSelectedModelReady)
        {
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
            this.PilotTranscriptTextBox.Text = "Modèle du profil ASR sélectionné manquant.";
            return;
        }

        var definition = SpeechRecognitionProfiles.Get(this.selectedSpeechProfile);
        this.transcriptionInProgress = true;
        this.transcriptionCancellation?.Cancel();
        this.transcriptionCancellation?.Dispose();
        this.transcriptionCancellation = new CancellationTokenSource();
        this.ExchangeLatencyText.Text = "TRANSCRIPTION...";
        this.PilotTranscriptTextBox.Text = $"{definition.ShortName} analyse le dernier PTT en français...";
        this.TranscribeLastButton.IsEnabled = false;
        this.CompareAsrButton.IsEnabled = false;

        try
        {
            var result = await this.speechRecognitionService.TranscribeAsync(audioPath, this.transcriptionCancellation.Token);
            if (acknowledgementOnly)
            {
                this.PilotTranscriptTextBox.Text = result.NormalizedText;
                this.ExchangeLatencyText.Text =
                    $"{result.ProcessingTime.TotalSeconds:F1} S - COLLATIONNEMENT REÇU";
                this.diagnosticsService.WriteEvent(
                    "RADIO",
                    $"Collationnement PTT accepté sans validation sémantique - transcription : {result.NormalizedText}");
            }
            else
            {
                this.ProcessPilotText(result.NormalizedText, fromMicrophone, result.ProcessingTime);
            }

            this.diagnosticsService.WriteEvent(
                "ASR",
                $"{result.ModelName} - inférence {result.ProcessingTime.TotalSeconds:F2} s - chargement {result.ModelLoadTime.TotalSeconds:F2} s - total {result.EndToEndTime.TotalSeconds:F2} s - {result.Segments.Count} segment(s) - {result.NormalizedText}");
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] {result.ModelName} : inférence {result.ProcessingTime.TotalSeconds:F1} s - total {result.EndToEndTime.TotalSeconds:F1} s.");
        }
        catch (OperationCanceledException)
        {
            this.ExchangeLatencyText.Text = "ANNULÉ";
        }
        catch (Exception exception)
        {
            var message = CleanMessage(exception);
            this.ExchangeLatencyText.Text = "ERREUR ASR";
            this.PilotTranscriptTextBox.Text = $"Transcription impossible : {message}";
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] ASR : {message}");
        }
        finally
        {
            this.transcriptionInProgress = false;
            this.UpdateWhisperStatus(this.speechRecognitionService.GetStatus(this.selectedSpeechProfile));
        }
    }

    private static async Task AwaitQuietlyAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Annulation normale lors d'un changement de profil ou de la fermeture.
        }
        catch
        {
            // L'erreur a déjà été journalisée par l'opération concernée.
        }
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 Mio";
        }

        return bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024.0 / 1024 / 1024:F2} Gio"
            : $"{bytes / 1024.0 / 1024:F1} Mio";
    }

    private void ProcessPilotText(string text, bool fromMicrophone, TimeSpan processingTime)
    {
        var cleanText = text.Trim();
        var analysis = PhraseologyService.Analyze(cleanText, this.latestSnapshot?.AircraftAtcId);
        var decision = this.groundOperationsCoordinator.Process(
            cleanText,
            this.latestSnapshot,
            this.currentOperationalFrequency);

        var response = decision.Action == ControllerAction.Silent
            ? $"[SILENCE] {decision.SystemMessage}"
            : decision.SpokenText;
        if (string.IsNullOrWhiteSpace(response))
        {
            response = $"[{decision.ReasonCode}] aucune réponse audio.";
        }

        this.PilotTranscriptTextBox.Text = cleanText;
        this.AnalysisText.Text = FormatAnalysis(analysis);
        this.ControllerResponseTextBox.Text = $"PHONIE : {response}";
        this.ExchangeLatencyText.Text = processingTime > TimeSpan.Zero
            ? $"{processingTime.TotalSeconds:F1} S - décision {decision.Confidence:P0}"
            : $"LAB - décision {decision.Confidence:P0}";
        this.UpdateGroundOperationsUi();

        var exchange = new RadioExchange(DateTimeOffset.Now, analysis, response, processingTime, fromMicrophone);
        this.SaveRadioExchange(exchange);
        this.diagnosticsService.WriteEvent(
            "RADIO",
            $"{(fromMicrophone ? "PTT" : "LAB")} - {FormatAnalysis(analysis)} - " +
            $"Décision {decision.ReasonCode} {decision.StateBefore}->{decision.StateAfter} - Réponse : {response}");

        if (decision.Action != ControllerAction.Silent && !string.IsNullOrWhiteSpace(decision.SpokenText))
        {
            this.StartControllerSpeech(decision.SpokenText, decision.RequiresAcknowledgement);
        }
    }

    private void SaveRadioExchange(RadioExchange exchange)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SessionsDirectory);
            var line = System.Text.Json.JsonSerializer.Serialize(exchange);
            var path = System.IO.Path.Combine(AppPaths.SessionsDirectory, $"radio-{DateTime.Now:yyyyMMdd}.jsonl");
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Export échange radio impossible : {CleanMessage(exception)}");
        }
    }

    private void UpdateAirportUi(AirportFacilityReport report)
    {
        var groundReport = this.latestGroundAirportReport;
        var radioReport = this.latestRadioAirportReport;
        if (groundReport is null
            && string.Equals(
                NormalizeIcao(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao),
                this.currentGeographicIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            groundReport = report;
        }

        if (radioReport is null
            && string.Equals(
                NormalizeIcao(string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao),
                this.currentRadioIcao,
                StringComparison.OrdinalIgnoreCase))
        {
            radioReport = report;
        }

        var geographicLabel = string.IsNullOrWhiteSpace(this.currentGeographicIcao)
            ? "SOL NON RÉSOLU"
            : groundReport is null
                ? $"SOL {this.currentGeographicIcao}"
                : $"{this.currentGeographicIcao} - {(string.IsNullOrWhiteSpace(groundReport.Name) ? "Aérodrome" : groundReport.Name)}";
        this.AirportNameText.Text = !string.IsNullOrWhiteSpace(this.currentRadioIcao)
            && !string.Equals(this.currentRadioIcao, this.currentGeographicIcao, StringComparison.OrdinalIgnoreCase)
                ? $"{geographicLabel} | RADIO {this.currentRadioIcao}"
                : geographicLabel;

        if (groundReport is null)
        {
            this.AirportSummaryText.Text = "Réseau sol en attente du rapport Facilities géographique.";
            this.SetForegroundResource(this.AirportSummaryText, "Warning");
        }
        else
        {
            var runwayStarts = groundReport.Starts.Count(item => item.Type == 1 && item.Number is >= 1 and <= 36);
            var diagnosticState = groundReport.DiagnosticSummary.TaxiPathBinaryLayoutValidated
                ? "DIAG TAXIPATH COHÉRENT"
                : "DIAG TAXIPATH À TRANSMETTRE";
            this.AirportSummaryText.Text =
                $"{groundReport.Runways.Count} piste(s) - {runwayStarts} seuil(s) - {groundReport.TaxiParkings.Count} parking(s) - {groundReport.TaxiPaths.Count} segment(s) taxi" +
                $" - {diagnosticState} ({groundReport.DiagnosticSummary.ParsedTaxiPathPacketCount}/{groundReport.DiagnosticSummary.TaxiPathPacketCount})" +
                (groundReport.ParseWarnings.Count > 0 ? $" - {groundReport.ParseWarnings.Count} avertissement(s)" : string.Empty);
            this.SetForegroundResource(
                this.AirportSummaryText,
                groundReport.DiagnosticSummary.TaxiPathBinaryLayoutValidated ? "Success" : "Warning");
        }

        var frequencyReport = radioReport ?? groundReport;
        if (frequencyReport is null)
        {
            this.FrequencySummaryText.Text = "Fréquences : en attente du contexte radio.";
            this.SetForegroundResource(this.FrequencySummaryText, "Warning");
        }
        else
        {
            var frequencyIcao = NormalizeIcao(
                string.IsNullOrWhiteSpace(frequencyReport.Icao)
                    ? frequencyReport.RequestedIcao
                    : frequencyReport.Icao);
            this.FrequencySummaryText.Text = $"Fréquences {frequencyIcao} : " + string.Join(" | ", frequencyReport.Frequencies
                .OrderBy(item => item.FrequencyMhz)
                .Select(item => $"{item.FrequencyMhz:F3}"));
            this.SetForegroundResource(
                this.FrequencySummaryText,
                frequencyReport.ParseWarnings.Count > 0 ? "Warning" : "Accent");
        }
    }

    private void UpdateAtisUi()
    {
        if (this.currentAtis is null)
        {
            this.AtisStateText.Text = "EN ATTENTE";
            this.AtisTextBox.Text = "L'ATIS sera généré après résolution de la station radio et réception des données disponibles.";
            return;
        }

        this.AtisStateText.Text = $"INFO {this.currentAtis.Letter.ToUpperInvariant()} - PISTE {this.currentAtis.Runway}";
        this.AtisTextBox.Text = this.currentAtis.Text;
    }

    private void UpdateGroundOperationsUi()
    {
        var state = this.groundOperationsCoordinator.GetUiState();
        this.GroundOperationsText.Text =
            $"SOL {state.SessionState.ToUpperInvariant()} - {state.Position} - piste {state.Runway} - " +
            $"attente {state.HoldShort} - route {state.Route} - occupation {state.Occupancy} - {state.AcknowledgementStatus}";
        this.GroundRoutingDiagnosticTextBox.Text =
            $"PROFIL : {state.ProfileStatus}{Environment.NewLine}" +
            $"COLLATIONNEMENT : {state.AcknowledgementStatus}{Environment.NewLine}{Environment.NewLine}" +
            state.Diagnostic;
        this.latestGroundMap = state.Map;
        this.GroundMapStatusText.Text = state.Map.Status.ToUpperInvariant();
        this.RenderGroundMap(state.Map);
        this.SetForegroundResource(
            this.GroundOperationsText,
            state.Occupancy.StartsWith("INCONNUE", StringComparison.OrdinalIgnoreCase)
                ? "Warning"
                : state.Confidence >= 0.75
                    ? "Success"
                    : "MutedText");
    }


    private async void RestartAsrButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        if (this.speechProfileRestartRequired)
        {
            var definition = SpeechRecognitionProfiles.Get(this.selectedSpeechProfile);
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] Redémarrage ASR impossible sans relancer PHONIE : " +
                $"le passage vers {definition.ShortName} change le runtime CPU/Vulkan.");
            this.WhisperStatusText.Text = "CHANGEMENT CPU/VULKAN - RELANCE DE PHONIE REQUISE";
            this.SetForegroundResource(this.WhisperStatusText, "Warning");
            return;
        }

        this.RestartAsrButton.IsEnabled = false;
        try
        {
            this.transcriptionCancellation?.Cancel();
            this.turboWarmupCancellation?.Cancel();
            this.benchmarkCancellation?.Cancel();
            await AwaitQuietlyAsync(this.turboWarmupTask);
            await AwaitQuietlyAsync(this.benchmarkTask);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
            while ((this.transcriptionInProgress || this.benchmarkInProgress)
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            if (this.transcriptionInProgress || this.benchmarkInProgress)
            {
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Redémarrage ASR reporté : worker encore actif.");
                return;
            }

            this.speechRecognitionService.ReleaseAllModels();
            _ = this.speechRecognitionService.SelectProfile(this.selectedSpeechProfile);
            this.UpdateWhisperStatus(this.speechRecognitionService.GetSelectedStatus());
            this.diagnosticsService.WriteEvent(
                "ASR_RESTART",
                $"Moteurs libérés et profil {SpeechRecognitionProfiles.Get(this.selectedSpeechProfile).ShortName} réinitialisé.");
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] Moteur ASR redémarré : modèles libérés, " +
                $"{SpeechRecognitionProfiles.Get(this.selectedSpeechProfile).ShortName} sera rechargé à la prochaine utilisation.");
            this.ScheduleTurboWarmup("redémarrage ASR demandé");
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Redémarrage ASR impossible : {CleanMessage(exception)}");
        }
        finally
        {
            this.RestartAsrButton.IsEnabled = true;
        }
    }

    private async void RestartControllerVoiceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.closing)
        {
            return;
        }

        this.RestartControllerVoiceButton.IsEnabled = false;
        try
        {
            this.speechSynthesisCancellation?.Cancel();
            await AwaitQuietlyAsync(this.controllerSpeechTask);
            this.speechSynthesisCancellation?.Dispose();
            this.speechSynthesisCancellation = null;
            this.audioService.StopPlayback();

            this.controllerSpeechService.LogMessage -= this.Service_OnLogMessage;
            this.controllerSpeechService.Dispose();
            this.controllerSpeechService = new ControllerSpeechService(this.audioService);
            this.controllerSpeechService.LogMessage += this.Service_OnLogMessage;

            this.diagnosticsService.WriteEvent("VOICE_RESTART", "Moteur de synthèse contrôleur réinitialisé.");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Moteur vocal contrôleur redémarré.");
            this.EnsureAtisCacheWhenNeeded();
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Redémarrage de la voix impossible : {CleanMessage(exception)}");
        }
        finally
        {
            this.RestartControllerVoiceButton.IsEnabled = true;
        }
    }

    private void GroundMapCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (this.latestGroundMap is not null)
        {
            this.RenderGroundMap(this.latestGroundMap);
        }
    }

    private void RenderGroundMap(GroundMapSnapshot map)
    {
        if (this.GroundMapCanvas is null)
        {
            return;
        }

        this.GroundMapCanvas.Children.Clear();
        var width = this.GroundMapCanvas.ActualWidth;
        var height = this.GroundMapCanvas.ActualHeight;
        if (width < 40 || height < 40 || map.Edges.Count == 0)
        {
            return;
        }

        var points = new List<(double X, double Z)>();
        points.AddRange(map.Edges.SelectMany(edge => new[]
        {
            (edge.FromX, edge.FromZ),
            (edge.ToX, edge.ToZ),
        }));
        points.AddRange(map.Nodes.Select(node => (node.X, node.Z)));
        points.AddRange(map.Traffic.Select(traffic => (traffic.X, traffic.Z)));
        if (map.AircraftX.HasValue && map.AircraftZ.HasValue)
        {
            points.Add((map.AircraftX.Value, map.AircraftZ.Value));
        }

        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minZ = points.Min(point => point.Z);
        var maxZ = points.Max(point => point.Z);
        var spanX = Math.Max(1, maxX - minX);
        var spanZ = Math.Max(1, maxZ - minZ);
        const double padding = 14;
        var scale = Math.Min(
            Math.Max(1, width - (padding * 2)) / spanX,
            Math.Max(1, height - (padding * 2)) / spanZ);

        Point Project(double x, double z) => new(
            padding + ((x - minX) * scale),
            height - padding - ((z - minZ) * scale));

        foreach (var edge in map.Edges
            .OrderBy(edge => edge.IsRoute)
            .ThenBy(edge => edge.IsOccupied))
        {
            var from = Project(edge.FromX, edge.FromZ);
            var to = Project(edge.ToX, edge.ToZ);
            var stroke = edge.IsOccupied
                ? Brushes.IndianRed
                : edge.IsRoute
                    ? Brushes.Gold
                    : edge.IsRunway
                        ? Brushes.DimGray
                        : Brushes.SlateGray;
            var thickness = edge.IsOccupied
                ? 5
                : edge.IsRoute
                    ? 4
                    : edge.IsRunway
                        ? 7
                        : 1.4;
            var line = new Line
            {
                X1 = from.X,
                Y1 = from.Y,
                X2 = to.X,
                Y2 = to.Y,
                Stroke = stroke,
                StrokeThickness = thickness,
                SnapsToDevicePixels = true,
                ToolTip =
                    $"Segment {edge.Id} - {edge.Kind} - {(string.IsNullOrWhiteSpace(edge.Name) ? "sans nom" : edge.Name)}" +
                    $"{(edge.IsOccupied ? " - OCCUPÉ" : string.Empty)}" +
                    $"{(edge.IsRoute ? " - ITINÉRAIRE" : string.Empty)}",
            };
            this.GroundMapCanvas.Children.Add(line);
        }

        foreach (var node in map.Nodes.Where(node =>
            node.IsSelected
            || node.IsOccupied
            || node.Kind.Contains("HoldShort", StringComparison.OrdinalIgnoreCase)
            || node.Kind.Contains("RunwayEntry", StringComparison.OrdinalIgnoreCase)))
        {
            var point = Project(node.X, node.Z);
            var radius = node.IsSelected ? 6.5 : 4.5;
            var fill = node.IsOccupied
                ? Brushes.IndianRed
                : node.IsSelected
                    ? Brushes.LimeGreen
                    : node.Kind.Contains("RunwayEntry", StringComparison.OrdinalIgnoreCase)
                        ? Brushes.DeepSkyBlue
                        : Brushes.DarkOrange;
            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = fill,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                ToolTip =
                    $"{node.Id} - {node.Kind} - {(string.IsNullOrWhiteSpace(node.Label) ? "sans nom" : node.Label)}" +
                    $"{(node.IsSelected ? " - SÉLECTIONNÉ" : string.Empty)}",
            };
            Canvas.SetLeft(ellipse, point.X - radius);
            Canvas.SetTop(ellipse, point.Y - radius);
            this.GroundMapCanvas.Children.Add(ellipse);

            if (!string.IsNullOrWhiteSpace(node.Label))
            {
                var label = new TextBlock
                {
                    Text = node.Label,
                    Foreground = Brushes.White,
                    Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
                    FontSize = 9,
                    Padding = new Thickness(2, 0, 2, 0),
                    ToolTip = node.Id,
                };
                Canvas.SetLeft(label, point.X + 6);
                Canvas.SetTop(label, point.Y - 11);
                this.GroundMapCanvas.Children.Add(label);
            }
        }

        foreach (var traffic in map.Traffic)
        {
            var point = Project(traffic.X, traffic.Z);
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = traffic.Classification.Contains("IGNORED", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.LightCoral
                    : Brushes.OrangeRed,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                ToolTip =
                    $"Objet {traffic.ObjectId} - {traffic.Callsign} - {traffic.Classification}",
            };
            Canvas.SetLeft(marker, point.X - 4);
            Canvas.SetTop(marker, point.Y - 4);
            this.GroundMapCanvas.Children.Add(marker);
        }

        if (map.AircraftX.HasValue && map.AircraftZ.HasValue)
        {
            var centre = Project(map.AircraftX.Value, map.AircraftZ.Value);
            var heading = map.AircraftHeadingDegrees * Math.PI / 180.0;
            Point Rotate(double localX, double localY)
            {
                var rotatedX = (localX * Math.Cos(heading)) - (localY * Math.Sin(heading));
                var rotatedY = (localX * Math.Sin(heading)) + (localY * Math.Cos(heading));
                return new Point(centre.X + rotatedX, centre.Y - rotatedY);
            }

            var aircraft = new Polygon
            {
                Points = new PointCollection
                {
                    Rotate(0, 10),
                    Rotate(-6, -7),
                    Rotate(0, -4),
                    Rotate(6, -7),
                },
                Fill = Brushes.DodgerBlue,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                ToolTip = "Avion utilisateur",
            };
            this.GroundMapCanvas.Children.Add(aircraft);
        }

        var legend = new TextBlock
        {
            Text = "JAUNE route  |  VERT attente  |  ORANGE candidats  |  CYAN entrée piste  |  ROUGE occupation  |  BLEU avion",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(175, 0, 0, 0)),
            FontSize = 8,
            Padding = new Thickness(3, 1, 3, 1),
        };
        Canvas.SetLeft(legend, 5);
        Canvas.SetTop(legend, 5);
        this.GroundMapCanvas.Children.Add(legend);
    }

    private void StartControllerSpeech(string text, bool requiresAcknowledgement = false)
    {
        this.speechSynthesisCancellation?.Cancel();
        this.speechSynthesisCancellation?.Dispose();
        this.speechSynthesisCancellation = new CancellationTokenSource();
        var token = this.speechSynthesisCancellation.Token;
        this.controllerSpeechTask = Task.Run(async () =>
        {
            try
            {
                await this.controllerSpeechService.SpeakControllerAsync(
                    text,
                    this.currentOperationalFrequency.ServiceName,
                    this.settings.OutputDeviceId,
                    token).ConfigureAwait(false);
                if (requiresAcknowledgement && !token.IsCancellationRequested)
                {
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        this.groundOperationsCoordinator.ArmAcknowledgement(DateTimeOffset.Now);
                        this.UpdateGroundOperationsUi();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Une nouvelle réponse remplace la synthèse précédente.
            }
            catch (Exception exception)
            {
                this.Service_OnLogMessage(
                    this,
                    $"Voix contrôleur indisponible : {CleanMessage(exception)}");
            }
        }, token);
    }

    private void EnsureAtisCacheWhenNeeded()
    {
        var information = this.currentAtis;
        this.PlayAtisButton.IsEnabled = information is not null;
        if (information is null
            || string.Equals(this.lastAtisAudioSignature, information.Signature, StringComparison.Ordinal))
        {
            return;
        }

        this.lastAtisAudioSignature = information.Signature;
        _ = Task.Run(async () =>
        {
            try
            {
                _ = await this.controllerSpeechService.EnsureAtisAsync(information).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                this.Service_OnLogMessage(
                    this,
                    $"Cache ATIS indisponible : {CleanMessage(exception)}");
            }
        });
    }

    private async void PlayAtisButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (this.currentAtis is null)
        {
            return;
        }

        try
        {
            await this.controllerSpeechService.PlayAtisAsync(
                this.currentAtis,
                this.settings.OutputDeviceId);
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Lecture ATIS impossible : {CleanMessage(exception)}");
        }
    }

    private void UpdateOperationalRadioUi()
    {
        this.StationText.Text = this.currentOperationalFrequency.ServiceName;
        this.RadioPolicyTitleText.Text = this.currentOperationalFrequency.Kind switch
        {
            OperationalRadioKind.Controlled => "SERVICE CONTRÔLÉ",
            OperationalRadioKind.InformationService => "SERVICE D'INFORMATION",
            OperationalRadioKind.AutomaticBroadcast => "DIFFUSION AUTOMATIQUE",
            OperationalRadioKind.RecordedMessage => "MESSAGE ENREGISTRÉ",
            OperationalRadioKind.SelfInformation => "AUTO-INFORMATION",
            _ => "FRÉQUENCE NON IDENTIFIÉE",
        };
        this.RadioPolicyText.Text = this.currentOperationalFrequency.Guidance;
        var resource = this.currentOperationalFrequency.Kind switch
        {
            OperationalRadioKind.Controlled => "Accent",
            OperationalRadioKind.InformationService => "Success",
            OperationalRadioKind.AutomaticBroadcast => "Accent",
            OperationalRadioKind.RecordedMessage => "Warning",
            OperationalRadioKind.SelfInformation => "Warning",
            _ => "MutedText",
        };
        this.SetForegroundResource(this.RadioPolicyTitleText, resource);
        this.RadioPolicyCard.SetResourceReference(Border.BorderBrushProperty, resource);
    }

    private static string FormatAnalysis(PilotMessageAnalysis analysis)
    {
        var callsignDetail = analysis.Callsign is null
            ? "-"
            : $"{analysis.Callsign} ({analysis.CallsignSource}, {analysis.CallsignConfidence:P0})";
        return $"Station {analysis.CalledStation ?? "-"} | Indicatif {callsignDetail} | Position {analysis.Position ?? "-"} | Intention {analysis.Intention ?? "-"} | ATIS {analysis.AtisLetter ?? "-"}" +
               (analysis.MissingCriticalFields.Count > 0 ? $" | Manque : {string.Join(", ", analysis.MissingCriticalFields)}" : string.Empty);
    }

    private static string CleanMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message.ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(message) ? exception.GetType().Name : message;
    }

    private void DiagnosticsService_OnSampleAvailable(object? sender, DiagnosticsSample sample)
    {
        _ = this.Dispatcher.BeginInvoke(() =>
        {
            this.CpuDiagnosticText.Text = $"{sample.CpuPercent:F2} %";
            this.CpuAverageDiagnosticText.Text = $"Moy. {sample.AverageCpuPercent:F2} % / max {sample.MaximumCpuPercent:F2} %";
            this.MemoryDiagnosticText.Text = $"{sample.WorkingSetMb:F0} Mo";
            this.ProcessDiagnosticText.Text = $"Max {sample.MaximumWorkingSetMb:F0} Mo / T {sample.ThreadCount} / H {sample.HandleCount}";
            this.TelemetryDiagnosticText.Text = $"{sample.SnapshotsPerSecond:F2} Hz";
        });
    }

    private void Service_OnLogMessage(object? sender, string message) =>
        _ = this.Dispatcher.BeginInvoke(() => this.AppendLog(message));

    private void AppendLog(string message)
    {
        this.diagnosticsService.WriteEvent("APP", message);

        // Some XAML controls can raise events while InitializeComponent is still
        // building the visual tree. The portable log is already available, but
        // the diagnostic TextBox may not exist yet.
        if (this.LogBox is null)
        {
            return;
        }

        if (this.LogBox.LineCount > 250)
        {
            var firstLineLength = this.LogBox.GetLineLength(0);
            this.LogBox.Select(0, firstLineLength + Environment.NewLine.Length);
            this.LogBox.SelectedText = string.Empty;
        }

        this.LogBox.AppendText(message + Environment.NewLine);
        this.LogBox.ScrollToEnd();
    }

    private void SelectThemeInUi()
    {
        this.suppressSettingsEvents = true;
        try
        {
            this.ThemeComboBox.SelectedIndex = string.Equals(this.settings.Theme, ThemeService.Light, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void SelectSpeechProfileInUi()
    {
        this.suppressSettingsEvents = true;
        try
        {
            this.SpeechProfileComboBox.SelectedItem = this.SpeechProfileComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), this.selectedSpeechProfile.ToString(), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void SelectGainInUi()
    {
        var allowedGains = new[] { 0, 3, 6, 9, 12, 15, 18 };
        var clampedGain = Math.Clamp(this.settings.MicrophoneGainDb, 0, 18);
        this.settings.MicrophoneGainDb = allowedGains
            .OrderBy(gain => Math.Abs(gain - clampedGain))
            .First();

        this.suppressSettingsEvents = true;
        try
        {
            this.GainComboBox.SelectedItem = this.GainComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), this.settings.MicrophoneGainDb.ToString(), StringComparison.Ordinal));
        }
        finally
        {
            this.suppressSettingsEvents = false;
        }
    }

    private void UpdatePttLabels()
    {
        this.PttKeyText.Text = $"CLAVIER : {KeyName(this.settings.PttVirtualKey).ToUpperInvariant()}";
        if (this.settings.JoystickPttButton is int button && !string.IsNullOrWhiteSpace(this.settings.JoystickPttDeviceName))
        {
            var status = this.joystickMappingAvailable ? "prêt" : "déconnecté";
            this.PttJoystickText.Text = $"HOTAS : {this.settings.JoystickPttDeviceName} - B{button} - {status}";
            this.SetForegroundResource(this.PttJoystickText, this.joystickMappingAvailable ? "Success" : "Warning");
            this.ClearJoystickPttButton.IsEnabled = true;
        }
        else
        {
            this.PttJoystickText.Text = "HOTAS : non assigné";
            this.SetForegroundResource(this.PttJoystickText, "MutedText");
            this.ClearJoystickPttButton.IsEnabled = false;
        }
    }

    private void RefreshVisualStates()
    {
        this.SetDotResource(this.StatusDot, StateResource(this.currentConnectionState));
        this.SetDotResource(this.SimFooterDot, StateResource(this.currentConnectionState));
        this.SetBackgroundResource(this.PttPanel, this.pttHeld ? "PttActiveBackground" : "PttIdleBackground");
        this.UpdatePttLabels();
        this.RefreshFooterState();
    }

    private void RefreshFooterState()
    {
        this.SetDotResource(this.AudioFooterDot, this.audioReady ? "Success" : "Danger");
        this.SetDotResource(this.PttFooterDot, this.pttHeld ? "Accent" : this.pttReady ? "Success" : "Danger");

        if (this.currentConnectionState == ConnectionState.Connected && this.audioReady && this.pttReady)
        {
            this.ReadyFooterText.Text = "Prêt";
            this.SetDotResource(this.ReadyFooterDot, "Success");
        }
        else if (this.audioReady && this.pttReady)
        {
            this.ReadyFooterText.Text = "En attente du simulateur";
            this.SetDotResource(this.ReadyFooterDot, "Warning");
        }
        else
        {
            this.ReadyFooterText.Text = "Initialisation incomplète";
            this.SetDotResource(this.ReadyFooterDot, "Danger");
        }
    }

    private void SaveSettings()
    {
        try
        {
            this.settingsService.Save(this.settings);
        }
        catch (Exception exception)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Réglages non enregistrés : {exception.Message}");
        }
    }

    private void SetDotResource(Shape dot, string resourceKey) => dot.SetResourceReference(Shape.FillProperty, resourceKey);

    private void SetBackgroundResource(FrameworkElement element, string resourceKey) => element.SetResourceReference(Border.BackgroundProperty, resourceKey);

    private void SetForegroundResource(FrameworkElement element, string resourceKey) => element.SetResourceReference(TextBlock.ForegroundProperty, resourceKey);

    private static string StateResource(ConnectionState state) => state switch
    {
        ConnectionState.Connected => "Success",
        ConnectionState.Error => "Danger",
        ConnectionState.Disconnected => "Danger",
        _ => "Warning",
    };

    private static bool DeviceExists(IReadOnlyList<AudioDeviceInfo> devices, string? id) =>
        !string.IsNullOrWhiteSpace(id) && devices.Any(device => device.Id == id);

    private static string NormalizeIcao(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 4 && normalized.All(char.IsAsciiLetterOrDigit)
            ? normalized
            : string.Empty;
    }

    private static string FriendlyStationType(string? stationType)
    {
        return stationType?.Trim().ToUpperInvariant() switch
        {
            "ATIS" => "ATIS",
            "AWS" => "MÉTÉO AUTO",
            "UNI" => "UNICOM",
            "CTAF" => "AUTO-INFO",
            "GND" => "SOL",
            "TWR" => "TOUR",
            "CLR" => "PRÉVOL",
            "APPR" => "APPROCHE",
            "DEP" => "DÉPART",
            "FSS" => "INFORMATION / AFIS",
            _ => "TYPE INCONNU",
        };
    }

    private static string FormatCoordinates(double latitude, double longitude)
    {
        var latHemisphere = latitude >= 0 ? "N" : "S";
        var lonHemisphere = longitude >= 0 ? "E" : "W";
        return $"{Math.Abs(latitude):F5}° {latHemisphere} - {Math.Abs(longitude):F5}° {lonHemisphere}";
    }

    private static string FormatDirection(double degrees) =>
        double.IsFinite(degrees) ? $"{degrees:000}°" : "-";

    private static string FormatWindSpeed(double knots) =>
        double.IsFinite(knots) ? $"{knots:F0} kt" : "-";

    private static string FormatQnh(double hpa) =>
        double.IsFinite(hpa) ? Math.Round(hpa, MidpointRounding.AwayFromZero).ToString("F0") : "-";

    private static string FormatTemperature(double celsius) =>
        double.IsFinite(celsius) ? $"{celsius:F0} °C" : "-";

    private static string FormatVisibility(double meters)
    {
        if (!double.IsFinite(meters) || meters <= 0)
        {
            return "-";
        }

        return meters >= 1000 ? $"{meters / 1000.0:F1} km" : $"{meters:F0} m";
    }

    private static string KeyName(int virtualKey)
    {
        return virtualKey switch
        {
            0xA2 => "Ctrl gauche",
            0xA3 => "Ctrl droit",
            0xA0 => "Maj gauche",
            0xA1 => "Maj droite",
            0xA4 => "Alt gauche",
            0xA5 => "Alt droit",
            0x20 => "Espace",
            0x0D => "Entrée",
            0x08 => "Retour arrière",
            _ => KeyInterop.KeyFromVirtualKey(virtualKey).ToString(),
        };
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private static T? FindVisualParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}

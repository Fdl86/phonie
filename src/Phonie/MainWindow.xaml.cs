using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private readonly SpeechRecognitionService speechRecognitionService;
    private readonly AtisService atisService = new();
    private readonly DispatcherTimer audioMeterTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
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
    private AirportFacilityReport? latestAirportReport;
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
    private bool transcriptionInProgress;
    private bool pseudoMaximized;
    private Rect normalBounds;

    public MainWindow()
    {
        this.settings = this.settingsService.Load();
        this.startupSpeechProfile = SpeechRecognitionProfiles.Parse(this.settings.SpeechRecognitionProfile);
        this.selectedSpeechProfile = this.startupSpeechProfile;
        this.speechRecognitionService = new SpeechRecognitionService(this.startupSpeechProfile);
        ThemeService.Apply(this.settings.Theme);

        this.InitializeComponent();
        this.suppressSettingsEvents = false;

        this.simConnectService.StatusChanged += this.SimConnectService_OnStatusChanged;
        this.simConnectService.SnapshotReceived += this.SimConnectService_OnSnapshotReceived;
        this.simConnectService.LogMessage += this.Service_OnLogMessage;
        this.simConnectService.AirportDataReceived += this.SimConnectService_OnAirportDataReceived;

        this.audioService.LogMessage += this.Service_OnLogMessage;
        this.audioService.RecordingStateChanged += this.AudioService_OnRecordingStateChanged;
        this.audioService.RecordingCompleted += this.AudioService_OnRecordingCompleted;

        this.keyboardHook.KeyPressed += this.KeyboardHook_OnKeyPressed;
        this.keyboardHook.KeyReleased += this.KeyboardHook_OnKeyReleased;

        this.joystickService.ButtonChanged += this.JoystickService_OnButtonChanged;
        this.joystickService.DevicesChanged += this.JoystickService_OnDevicesChanged;
        this.joystickService.LogMessage += this.Service_OnLogMessage;

        this.diagnosticsService.SampleAvailable += this.DiagnosticsService_OnSampleAvailable;
        this.speechRecognitionService.StatusChanged += this.WhisperService_OnStatusChanged;
        this.audioMeterTimer.Tick += this.AudioMeterTimer_OnTick;

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
        this.simConnectService.Start();
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Log portable : {this.diagnosticsService.SessionFilePath}");
        var startupDefinition = SpeechRecognitionProfiles.Get(this.startupSpeechProfile);
        var runtimeOrder = this.speechRecognitionService.StartupWhisperUsesVulkan ? "Vulkan puis CPU" : "CPU";
        this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Profil ASR au démarrage : {startupDefinition.ShortName} - runtime Whisper {runtimeOrder}.");
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
        this.keyboardHook.Dispose();
        await this.joystickService.DisposeAsync();
        this.transcriptionCancellation?.Cancel();
        this.transcriptionCancellation?.Dispose();
        this.speechRecognitionService.Dispose();
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
        this.AirportDataText.Text = "Airport Data : lecture LFBI...";
        if (!this.simConnectService.RequestAirportData("LFBI"))
        {
            this.AirportDataText.Text = "Airport Data : SimConnect indisponible";
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Airport Data LFBI : attendre la connexion au simulateur.");
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
        if (this.transcriptionInProgress)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Une reconnaissance est déjà en cours.");
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
        var input = this.InputDeviceComboBox.SelectedItem as AudioDeviceInfo;
        if (this.audioService.StartRecording(input?.Id, this.settings.MicrophoneGainDb))
        {
            this.diagnosticsService.WriteEvent("PTT", $"Début - {sourceLabel}");
            return;
        }

        this.activePttSources.Clear();
        this.activePttSourceLabels.Clear();
        this.pttHeld = false;
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
                return;
            }

            this.lastRecordingPath = result.FilePath;
            this.PlayLastRecordingButton.IsEnabled = result.FileSizeBytes > 44;
            this.TranscribeLastButton.IsEnabled = result.FileSizeBytes > 44 && this.speechRecognitionService.IsSelectedModelReady && !this.speechProfileRestartRequired;
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
                }
                else if (this.speechRecognitionService.IsSelectedModelReady)
                {
                    await this.TranscribeAndProcessAsync(result.FilePath, true);
                }
                else
                {
                    this.PilotTranscriptTextBox.Text = "PTT enregistré. Installez le modèle du profil ASR sélectionné.";
                    this.ExchangeLatencyText.Text = "MODÈLE MANQUANT";
                }
            }
        });
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
            this.RefreshFooterState();
        });
    }

    private void SimConnectService_OnSnapshotReceived(object? sender, SimulatorSnapshot snapshot)
    {
        this.diagnosticsService.ReportSnapshot();
        this.latestSnapshot = snapshot;


        this.currentOperationalFrequency = OperationalRadioService.Resolve(snapshot, this.latestAirportReport);
        this.currentAtis = this.atisService.Update(snapshot, this.latestAirportReport);

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
            this.DistanceText.Text = $"Distance LFBI : {snapshot.DistanceToLfbiNm:F1} NM";
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
            this.ComMetaText.Text = $"{stationIdent} - {stationType} - {spacing} - {receive}{radioStatus}";
            this.UpdateOperationalRadioUi();

            this.WeatherPrimaryText.Text = $"Vent {FormatDirection(snapshot.WindDirectionTrueDegrees)} / {FormatWindSpeed(snapshot.WindVelocityKnots)} - QNH {FormatQnh(snapshot.QnhHpa)}";
            this.WeatherSecondaryText.Text = $"Température {FormatTemperature(snapshot.TemperatureCelsius)} - visibilité {FormatVisibility(snapshot.VisibilityMeters)}";
            this.UpdateAtisUi();

            var radioSignature = $"{snapshot.Com1ActiveMhz:F3}|{this.currentOperationalFrequency.ServiceName}|{this.currentOperationalFrequency.Kind}|{snapshot.Com1SpacingMode}|{snapshot.Com1Receiving}";
            if (!string.Equals(radioSignature, this.lastRadioSignature, StringComparison.Ordinal))
            {
                this.lastRadioSignature = radioSignature;
                this.AppendLog($"[{DateTime.Now:HH:mm:ss}] COM1 : {snapshot.Com1ActiveMhz:F3} - {this.currentOperationalFrequency.ServiceName} - {this.currentOperationalFrequency.Guidance}");
            }
        });
    }

    private void SimConnectService_OnAirportDataReceived(object? sender, AirportFacilityReport report)
    {
        this.latestAirportReport = report;
        if (this.latestSnapshot is not null)
        {
            this.currentOperationalFrequency = OperationalRadioService.Resolve(this.latestSnapshot, report);
            this.currentAtis = this.atisService.Update(this.latestSnapshot, report);
        }

        _ = this.Dispatcher.BeginInvoke(() =>
        {
            var icao = string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao;
            this.AirportDataText.Text = $"{icao} : {report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s)";
            this.UpdateAirportUi(report);
            this.UpdateOperationalRadioUi();
            this.UpdateAtisUi();
            this.AppendLog(
                $"[{DateTime.Now:HH:mm:ss}] Airport Data {icao} terminé : " +
                $"{report.Runways.Count} piste(s), {report.Frequencies.Count} fréquence(s), " +
                $"{report.TaxiParkings.Count} parking(s), {report.TaxiPaths.Count} chemin(s), " +
                $"{report.ParseWarnings.Count} avertissement(s). " +
                $"Fichier : logs\\airport-data\\{System.IO.Path.GetFileName(report.TextPath)}");
        });
    }

    private void WhisperService_OnStatusChanged(object? sender, SpeechModelStatus status) =>
        _ = this.Dispatcher.BeginInvoke(() => this.UpdateWhisperStatus(status));

    private void UpdateWhisperStatus(SpeechModelStatus status)
    {
        var definition = SpeechRecognitionProfiles.Get(this.selectedSpeechProfile);
        this.WhisperStatusText.Text = status.Message;
        this.WhisperProgressBar.Value = Math.Clamp(status.ProgressPercent, 0, 100);
        this.WhisperProgressBar.Visibility = status.State == SpeechModelState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        var busy = status.State is SpeechModelState.Downloading or SpeechModelState.Loading or SpeechModelState.Transcribing;
        var modelReady = this.speechRecognitionService.IsModelReady(this.selectedSpeechProfile);
        this.DownloadWhisperButton.IsEnabled = !busy && !this.speechProfileRestartRequired;
        this.DownloadWhisperButton.Content = this.speechProfileRestartRequired
            ? "Redémarrer"
            : modelReady
                ? "Profil installé"
                : "Installer profil";
        this.TranscribeLastButton.IsEnabled = modelReady
            && !this.speechProfileRestartRequired
            && !this.transcriptionInProgress
            && !string.IsNullOrWhiteSpace(this.lastRecordingPath)
            && File.Exists(this.lastRecordingPath);
        this.CompareAsrButton.IsEnabled = !this.transcriptionInProgress
            && !string.IsNullOrWhiteSpace(this.lastRecordingPath)
            && File.Exists(this.lastRecordingPath);

        var resource = status.State switch
        {
            SpeechModelState.Ready => "Success",
            SpeechModelState.Downloading or SpeechModelState.Loading or SpeechModelState.Transcribing => "Accent",
            SpeechModelState.RestartRequired => "Warning",
            SpeechModelState.Error => "Danger",
            _ => "Warning",
        };
        this.SetForegroundResource(this.WhisperStatusText, resource);
        this.DownloadWhisperButton.ToolTip = definition.Description;
    }

    private async Task TranscribeAndProcessAsync(string audioPath, bool fromMicrophone)
    {
        if (this.transcriptionInProgress)
        {
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] Une reconnaissance est déjà en cours.");
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
            this.ProcessPilotText(result.NormalizedText, fromMicrophone, result.ProcessingTime);
            this.diagnosticsService.WriteEvent(
                "ASR",
                $"{result.ModelName} - {result.ProcessingTime.TotalSeconds:F2} s - {result.Segments.Count} segment(s) - {result.NormalizedText}");
            this.AppendLog($"[{DateTime.Now:HH:mm:ss}] {result.ModelName} : transcription terminée en {result.ProcessingTime.TotalSeconds:F1} s.");
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

    private void ProcessPilotText(string text, bool fromMicrophone, TimeSpan processingTime)
    {
        var cleanText = text.Trim();
        var analysis = PhraseologyService.Analyze(cleanText, this.latestSnapshot?.AircraftAtcId);
        var response = PhraseologyService.BuildFirstContactResponse(
            analysis,
            this.currentOperationalFrequency,
            this.currentAtis,
            this.latestSnapshot);

        this.PilotTranscriptTextBox.Text = cleanText;
        this.AnalysisText.Text = FormatAnalysis(analysis);
        this.ControllerResponseTextBox.Text = $"PHONIE : {response}";
        this.ExchangeLatencyText.Text = processingTime > TimeSpan.Zero
            ? $"{processingTime.TotalSeconds:F1} S - {analysis.Confidence:P0}"
            : $"LAB - {analysis.Confidence:P0}";

        var exchange = new RadioExchange(DateTimeOffset.Now, analysis, response, processingTime, fromMicrophone);
        this.SaveRadioExchange(exchange);
        this.diagnosticsService.WriteEvent(
            "RADIO",
            $"{(fromMicrophone ? "PTT" : "LAB")} - {FormatAnalysis(analysis)} - Réponse : {response}");
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
        var icao = string.IsNullOrWhiteSpace(report.Icao) ? report.RequestedIcao : report.Icao;
        var name = string.IsNullOrWhiteSpace(report.Name) ? "Aérodrome" : report.Name;
        this.AirportNameText.Text = $"{icao} - {name}";
        var runwayStarts = report.Starts.Count(item => item.Type == 1 && item.Number is >= 1 and <= 36);
        this.AirportSummaryText.Text =
            $"{report.Runways.Count} piste(s) - {runwayStarts} seuil(s) exploitables - {report.TaxiParkings.Count} parking(s) - {report.TaxiPaths.Count} segment(s) taxi" +
            (report.ParseWarnings.Count > 0 ? $" - {report.ParseWarnings.Count} avertissement(s)" : string.Empty);
        this.FrequencySummaryText.Text = "Fréquences : " + string.Join(" | ", report.Frequencies
            .OrderBy(item => item.FrequencyMhz)
            .Select(item => $"{item.FrequencyMhz:F3}"));
        this.SetForegroundResource(this.FrequencySummaryText, report.ParseWarnings.Count > 0 ? "Warning" : "Accent");
    }

    private void UpdateAtisUi()
    {
        if (this.currentAtis is null)
        {
            this.AtisStateText.Text = "EN ATTENTE";
            this.AtisTextBox.Text = "L'ATIS sera généré après lecture de LFBI et réception de la météo locale.";
            return;
        }

        this.AtisStateText.Text = $"INFO {this.currentAtis.Letter.ToUpperInvariant()} - PISTE {this.currentAtis.Runway}";
        this.AtisTextBox.Text = this.currentAtis.Text;
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
